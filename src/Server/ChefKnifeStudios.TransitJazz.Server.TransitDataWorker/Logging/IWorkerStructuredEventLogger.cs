namespace ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Logging;

public interface IWorkerStructuredEventLogger
{
    void Emit(StructuredLogEvent logEvent);

    void EmitRecovery(StructuredLogEvent recoveryEvent, string recoveredEventName, string? recoveredReasonCode);
}

public sealed class NullWorkerStructuredEventLogger : IWorkerStructuredEventLogger
{
    public static readonly NullWorkerStructuredEventLogger Instance = new();
    private NullWorkerStructuredEventLogger() { }
    public void Emit(StructuredLogEvent logEvent) { }
    public void EmitRecovery(StructuredLogEvent recoveryEvent, string recoveredEventName, string? recoveredReasonCode) { }
}

