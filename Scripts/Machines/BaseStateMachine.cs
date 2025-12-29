using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ExtDebugLogger;

namespace ExtState
{
    public abstract class BaseStateMachine
    {
        protected IState ActiveState;
        protected readonly List<IState> StatesList = new();
        private readonly Dictionary<Type, IState> _states = new();

        public abstract Enum LogTag { get; }

        #region Enter

        protected async UniTask Enter<TState>() where TState : class, IState, IEnterableState
        {
            var state = await ChangeState<TState>();

            Logger.Log($"Entering state {typeof(TState).Name}", LogTag);

            await state.Enter();
        }
        
        protected async UniTask Enter(Type type)
        {
            var state = await ChangeState(type);

            Logger.Log($"Entering state {type.Name}", LogTag);

            if (state is IEnterableState enterableState) await enterableState.Enter();
        }

        protected async UniTask Enter<TState, TPayload>(TPayload payload) where TState : class, IState, IPayloadedState<TPayload>
        {
            var state = await ChangeState<TState>();

            Logger.Log($"Entering state {typeof(TState).Name} with payload: \n{typeof(TPayload).Name}: {payload}", LogTag);

            await state.Enter(payload);
        }
        
        protected async UniTask Enter(int index)
        {
            var state = await ChangeState(index);

            Logger.Log($"Entering state {state.GetType().Name}", LogTag);

            if (state is IEnterableState enterableState) await enterableState.Enter();
        }

        #endregion

        #region ChangeState

        private async UniTask<TState> ChangeState<TState>() where TState : class, IState
        {
            if (ActiveState != null) await ActiveState.Exit();

            var state = GetState<TState>();
            ActiveState = state;

            return state;
        }
        
        private async UniTask<IState> ChangeState(Type type)
        {
            if (ActiveState != null) await ActiveState.Exit();

            var state = GetState(type);
            if (state is null)
                return null;
            
            ActiveState = state;

            return state;
        }

        private async UniTask<IState> ChangeState(int index)
        {
            if (ActiveState != null) await ActiveState.Exit();

            var state = GetState(index);
            ActiveState = state;

            return state;
        }

        #endregion

        #region GetStates

        private TState GetState<TState>() where TState : class, IState
        {
            return _states[typeof(TState)] as TState;
        }

        private IState GetState(Type type)
        {
            return _states.GetValueOrDefault(type);
        }
        
        private IState GetState(int index)
        {
            return StatesList[index];
        }

        #endregion
        
        protected void RegisterState<TState>(TState state) where TState : IState
        {
            _states.Add(typeof(TState), state);
            StatesList.Add(state);
        }
    }
}