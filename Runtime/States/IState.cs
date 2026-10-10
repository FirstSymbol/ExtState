using Cysharp.Threading.Tasks;

namespace ExtState
{
    public interface IState
    {
        public UniTask Exit();
    }
}