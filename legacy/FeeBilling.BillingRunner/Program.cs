using System;
using System.ServiceProcess;

namespace FeeBilling.BillingRunner
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            log4net.Config.XmlConfigurator.Configure();

            // Debugging aid: FeeBilling.BillingRunner.exe --console
            if (Environment.UserInteractive && args.Length > 0 && args[0] == "--console")
            {
                var service = new BillingRunnerService();
                service.StartInteractive(args);
                Console.WriteLine("BillingRunner running. Press Enter to stop.");
                Console.ReadLine();
                service.StopInteractive();
                return;
            }

            ServiceBase.Run(new ServiceBase[] { new BillingRunnerService() });
        }
    }
}
