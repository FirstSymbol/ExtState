using System.Threading;
using Cysharp.Threading.Tasks;

namespace ExtState
{
  /// <summary>A state of a <see cref="StateMachine"/>.</summary>
  public interface IState
  {
    /// <summary>Called when the machine leaves the state.</summary>
    UniTask Exit(CancellationToken cancellationToken);
  }

  /// <summary>A state entered without data.</summary>
  public interface IEnterState : IState
  {
    UniTask Enter(CancellationToken cancellationToken);
  }

  /// <summary>A state entered with a payload.</summary>
  public interface IPayloadState<in TPayload> : IState
  {
    UniTask Enter(TPayload payload, CancellationToken cancellationToken);
  }

  /// <summary>Receives <see cref="StateMachine.Tick"/> while the state is active.</summary>
  public interface IUpdatableState
  {
    void Tick(float deltaTime);
  }

  /// <summary>Convenience base for states without payload: override only what you need.</summary>
  public abstract class State : IEnterState
  {
    public virtual UniTask Enter(CancellationToken cancellationToken) => UniTask.CompletedTask;
    public virtual UniTask Exit(CancellationToken cancellationToken) => UniTask.CompletedTask;
  }

  /// <summary>Convenience base for states with a payload.</summary>
  public abstract class PayloadState<TPayload> : IPayloadState<TPayload>
  {
    public abstract UniTask Enter(TPayload payload, CancellationToken cancellationToken);
    public virtual UniTask Exit(CancellationToken cancellationToken) => UniTask.CompletedTask;
  }
}
