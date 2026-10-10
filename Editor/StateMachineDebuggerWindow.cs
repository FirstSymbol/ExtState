using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtState.Editor
{
  /// <summary>Live view of state machines: current state, registered states and transition history.</summary>
  internal sealed class StateMachineDebuggerWindow : EditorWindow
  {
    private static readonly Color Accent = new(0.3f, 0.55f, 0.85f);
    private static readonly Color Busy = new(0.88f, 0.55f, 0.18f);
    private static readonly Color Error = new(0.86f, 0.3f, 0.3f);

    private readonly List<StateMachine> _machines = new();
    private readonly List<StateTransitionRecord> _history = new();

    private ListView _machineList;
    private Label _title;
    private Label _badge;
    private Label _details;
    private VisualElement _states;
    private MultiColumnListView _historyTable;
    private Label _empty;
    private StateMachine _selected;
    private int _historySignature = -1;

    [MenuItem("Window/ExtState/State Machine Debugger", priority = 2020)]
    public static void Open()
    {
      var window = GetWindow<StateMachineDebuggerWindow>();
      window.titleContent = new GUIContent("State Machines", EditorGUIUtility.IconContent("d_AnimatorStateMachine Icon").image);
      window.minSize = new Vector2(560, 260);
    }

    public void CreateGUI()
    {
      var toolbar = new Toolbar();
      toolbar.Add(new Label("Live state machines (Play Mode and editor code)") { style = { unityTextAlign = TextAnchor.MiddleLeft, marginLeft = 4 } });
      toolbar.Add(new ToolbarSpacer { flex = true });
      toolbar.Add(new ToolbarButton(Refresh) { text = "Refresh" });
      rootVisualElement.Add(toolbar);

      var split = new TwoPaneSplitView(0, 200, TwoPaneSplitViewOrientation.Horizontal);
      rootVisualElement.Add(split);

      _machineList = new ListView
      {
        itemsSource = _machines,
        fixedItemHeight = 34,
        selectionType = SelectionType.Single,
        makeItem = MakeMachineItem,
        bindItem = BindMachineItem
      };
      _machineList.selectionChanged += selection =>
      {
        _selected = selection.FirstOrDefault() as StateMachine;
        _historySignature = -1;
        Refresh();
      };
      split.Add(_machineList);

      var detail = new VisualElement { style = { flexGrow = 1, paddingLeft = 8, paddingRight = 8, paddingTop = 6 } };
      var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
      _title = new Label { style = { fontSize = 14, unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1 } };
      _badge = Badge();
      header.Add(_title);
      header.Add(_badge);
      detail.Add(header);

      _details = new Label { style = { opacity = 0.7f, marginBottom = 6 } };
      detail.Add(_details);

      detail.Add(new Label("States") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
      _states = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginBottom = 6 } };
      detail.Add(_states);

      detail.Add(new Label("History") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
      _historyTable = CreateHistoryTable();
      detail.Add(_historyTable);

      _empty = new Label("Select a state machine.") { style = { opacity = 0.6f, marginTop = 20, unityTextAlign = TextAnchor.MiddleCenter } };
      detail.Add(_empty);
      split.Add(detail);

      StateMachineRegistry.Changed += RefreshMachines;
      rootVisualElement.RegisterCallback<DetachFromPanelEvent>(_ => StateMachineRegistry.Changed -= RefreshMachines);
      rootVisualElement.schedule.Execute(Refresh).Every(250);
      RefreshMachines();
    }

    private static Label Badge()
    {
      var badge = new Label
      {
        style =
        {
          paddingLeft = 8, paddingRight = 8, paddingTop = 2, paddingBottom = 2,
          borderTopLeftRadius = 9, borderTopRightRadius = 9, borderBottomLeftRadius = 9, borderBottomRightRadius = 9,
          color = Color.white, unityFontStyleAndWeight = FontStyle.Bold, fontSize = 11
        }
      };
      return badge;
    }

    private static VisualElement MakeMachineItem()
    {
      var item = new VisualElement { style = { paddingLeft = 6, justifyContent = Justify.Center } };
      item.Add(new Label { name = "name", style = { unityFontStyleAndWeight = FontStyle.Bold } });
      item.Add(new Label { name = "state", style = { opacity = 0.7f, fontSize = 11 } });
      return item;
    }

    private void BindMachineItem(VisualElement item, int index)
    {
      StateMachine machine = _machines[index];
      item.Q<Label>("name").text = machine.Name;
      item.Q<Label>("state").text = machine.IsTransitioning
        ? "transitioning…"
        : machine.CurrentState?.GetType().Name ?? "no state";
    }

    private MultiColumnListView CreateHistoryTable()
    {
      var table = new MultiColumnListView
      {
        itemsSource = _history,
        fixedItemHeight = 20,
        selectionType = SelectionType.None,
        showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
        style = { flexGrow = 1, minHeight = 80 }
      };

      table.columns.Add(new Column
      {
        title = "Time", width = 70,
        makeCell = () => new Label(),
        bindCell = (e, i) => ((Label)e).text = $"{_history[i].StartTime:0.00}s"
      });
      table.columns.Add(new Column
      {
        title = "Transition", width = 220, stretchable = true,
        makeCell = () => new Label(),
        bindCell = (e, i) => ((Label)e).text = $"{_history[i].From}  →  {_history[i].To}"
      });
      table.columns.Add(new Column
      {
        title = "Duration", width = 70,
        makeCell = () => new Label(),
        bindCell = (e, i) => ((Label)e).text = $"{_history[i].Duration * 1000.0:0} ms"
      });
      table.columns.Add(new Column
      {
        title = "Error", width = 160, stretchable = true,
        makeCell = () => new Label { style = { color = Error } },
        bindCell = (e, i) => ((Label)e).text = _history[i].Error ?? string.Empty
      });
      return table;
    }

    private void RefreshMachines()
    {
      if (_machineList == null)
        return;

      StateMachineRegistry.GetMachines(_machines);
      _machineList.RefreshItems();
      if (_selected != null && !_machines.Contains(_selected))
      {
        _selected = null;
        _machineList.ClearSelection();
      }
    }

    private void Refresh()
    {
      if (_machineList == null)
        return;

      RefreshMachines();
      bool hasSelection = _selected != null;
      _empty.style.display = hasSelection ? DisplayStyle.None : DisplayStyle.Flex;
      _historyTable.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;
      _title.text = hasSelection ? _selected.Name : _machines.Count == 0 ? "No state machines" : string.Empty;
      _badge.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;
      _states.Clear();

      if (!hasSelection)
      {
        _details.text = _machines.Count == 0 ? "Machines appear here while they are alive." : string.Empty;
        return;
      }

      IState current = _selected.CurrentState;
      _badge.text = _selected.IsTransitioning ? "Transitioning" : current?.GetType().Name ?? "No state";
      _badge.style.backgroundColor = _selected.IsTransitioning ? Busy : current != null ? Accent : new Color(0.5f, 0.5f, 0.5f);
      _details.text = $"{_selected.GetType().Name}  ·  previous: {_selected.PreviousState?.GetType().Name ?? "—"}  ·  " +
                      $"reenter: {_selected.ReenterPolicy}";

      foreach (IState state in _selected.States)
      {
        bool active = ReferenceEquals(state, current);
        var chip = new Label(state.GetType().Name)
        {
          style =
          {
            paddingLeft = 8, paddingRight = 8, paddingTop = 2, paddingBottom = 2, marginRight = 4, marginBottom = 4,
            borderTopLeftRadius = 9, borderTopRightRadius = 9, borderBottomLeftRadius = 9, borderBottomRightRadius = 9,
            borderLeftWidth = 1, borderRightWidth = 1, borderTopWidth = 1, borderBottomWidth = 1,
            borderLeftColor = Accent, borderRightColor = Accent, borderTopColor = Accent, borderBottomColor = Accent,
            backgroundColor = active ? Accent : Color.clear,
            color = active ? Color.white : StyleKeyword.Null,
            unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal
          }
        };
        _states.Add(chip);
      }

      int signature = _selected.History.Count == 0
        ? 0
        : _selected.History.Count * 397 ^ _selected.History[_selected.History.Count - 1].StartTime.GetHashCode();
      if (signature != _historySignature)
      {
        _historySignature = signature;
        _history.Clear();
        _history.AddRange(_selected.History.Reverse());
        _historyTable.RefreshItems();
      }
    }
  }
}
