using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

namespace FeeBilling.BillingRunner
{
    // installutil.exe FeeBilling.BillingRunner.exe
    [RunInstaller(true)]
    public class ProjectInstaller : Installer
    {
        public ProjectInstaller()
        {
            var processInstaller = new ServiceProcessInstaller
            {
                // Runs as the FEEBILLING\svc-billing domain account (MSDTC needs network access).
                Account = ServiceAccount.User
            };

            var serviceInstaller = new ServiceInstaller
            {
                ServiceName = "FeeBilling.BillingRunner",
                DisplayName = "FeeBilling Billing Runner",
                Description = "Processes queued billing runs (BillingRunQueue) every 30 seconds.",
                StartType = ServiceStartMode.Automatic
            };

            Installers.Add(processInstaller);
            Installers.Add(serviceInstaller);
        }
    }
}
