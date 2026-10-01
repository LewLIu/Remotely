using System.ServiceProcess;

namespace Remotely.Manager.Win.Services;

public static class ServiceStartupPolicy
{
    public static bool RequiresManualConfiguration(ServiceStartMode startType)
        => startType != ServiceStartMode.Manual;
}
