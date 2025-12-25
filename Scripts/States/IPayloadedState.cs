using Cysharp.Threading.Tasks;

namespace ExtState
{
    public interface IPayloadedState<in TPayload>
    {
        UniTask Enter(TPayload payload);
    }
}