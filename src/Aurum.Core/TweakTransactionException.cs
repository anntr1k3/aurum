namespace Aurum.Core;

public sealed class TweakTransactionException : Exception
{
    public TweakTransactionException(string message, Exception operationError, IReadOnlyList<Exception> recoveryErrors)
        : base(message, operationError)
    {
        RecoveryErrors = recoveryErrors;
    }

    public IReadOnlyList<Exception> RecoveryErrors { get; }

    public bool RecoverySucceeded => RecoveryErrors.Count == 0;
}
