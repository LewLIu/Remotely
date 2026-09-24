namespace Remotely.Manager.Win.Services;

public enum RemotelyServiceState
{
    Stopped,
    StartPending,
    Running,
    StopPending,
    Missing,
    Error
}
