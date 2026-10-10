using System;

namespace ExtState
{
  /// <summary>Creates states registered by type through <see cref="StateMachine.Register{TState}()"/>.</summary>
  public interface IStateFactory
  {
    IState Create(Type stateType, IStateMachine machine);
  }

  /// <summary>
  /// Factory backed by a delegate, so any DI container can create states:
  /// <c>new DelegateStateFactory((type, machine) => (IState)container.Instantiate(type, new object[] { machine }))</c>.
  /// </summary>
  public sealed class DelegateStateFactory : IStateFactory
  {
    private readonly Func<Type, IStateMachine, IState> _create;

    public DelegateStateFactory(Func<Type, IStateMachine, IState> create)
    {
      _create = create ?? throw new ArgumentNullException(nameof(create));
    }

    /// <summary>Uses a resolver that ignores the machine, e.g. <c>type => container.Resolve(type)</c>.</summary>
    public DelegateStateFactory(Func<Type, object> resolve)
    {
      if (resolve == null)
        throw new ArgumentNullException(nameof(resolve));
      _create = (type, _) => (IState)resolve(type);
    }

    public IState Create(Type stateType, IStateMachine machine) => _create(stateType, machine);
  }

  /// <summary>
  /// Creates states with <see cref="Activator"/>: uses a constructor taking the machine
  /// (<see cref="IStateMachine"/> or the concrete machine type) when present, otherwise the parameterless one.
  /// </summary>
  public sealed class ActivatorStateFactory : IStateFactory
  {
    public static readonly ActivatorStateFactory Instance = new();

    public IState Create(Type stateType, IStateMachine machine)
    {
      foreach (var constructor in stateType.GetConstructors())
      {
        var parameters = constructor.GetParameters();
        if (parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(machine))
          return (IState)constructor.Invoke(new object[] { machine });
      }

      return (IState)Activator.CreateInstance(stateType);
    }
  }
}
