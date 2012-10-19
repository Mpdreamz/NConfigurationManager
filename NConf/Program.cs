using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;

namespace NConf
{
  class Program
  {
    static void Main(string[] args)
    {
      NConfiguration.NConfigurationManager.Initialize();
      if (args == null || !args.Any())
      {
        Console.WriteLine(NConfiguration.NConfigurationManager.GetEnvironment());
      }
      else if (args.Count() != 2 || (args[0] != "-appSetting" && args[0] != "-connectionString") && (args[0] != "-a" && args[0] != "-c"))
      {
        Console.Error.WriteLine("Usage: nconf [option] [key]");
        Console.Error.WriteLine("\t: -appSetting (-a) [KEY]: prints the appseting value for [KEY]");
        Console.Error.WriteLine("\t: -connectionString (-c) [KEY]: prints the connectionString value for [KEY]");
      }
      else if (args[0].StartsWith("-a"))
      {
        var key = args[1];
        Console.WriteLine(ConfigurationManager.AppSettings[key]);
      }
      else if (args[0].StartsWith("-c"))
      {
        var key = args[1];
        Console.WriteLine(ConfigurationManager.ConnectionStrings[key].ConnectionString);
      }
    }
  }
}
