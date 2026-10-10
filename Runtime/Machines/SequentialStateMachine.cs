using Cysharp.Threading.Tasks;

namespace ExtState
{
    public abstract class SequentialStateMachine : BaseStateMachine
    {
        public async UniTask NextState()
        {
            var indexOfActiveState = -1;
            if (ActiveState != null)
            {
                indexOfActiveState = StatesList.IndexOf(ActiveState);
                if (indexOfActiveState == StatesList.Count - 1)
                {
                    await ActiveState.Exit();
                    ActiveState = null;
                    return;
                }
            }

            await Enter(indexOfActiveState + 1);
        }
    }
}