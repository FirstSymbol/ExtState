using Cysharp.Threading.Tasks;

namespace ExtState
{
    public interface IEnterableState
    {
        UniTask Enter();
        
    }
}