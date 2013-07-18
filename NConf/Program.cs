using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;

namespace NConf
{
  class Program
  {
    private static readonly string[] _validSwitches = new string[]
    {
      "-appSetting", "-a", "-connectionString", "-c", "-validate", "-v", "-list","-l"
    };

    static int Main(string[] args)
    {
      if (args == null || !args.Any())
      {
        NConfiguration.NConfigurationManager.Initialize();
        Console.WriteLine(NConfiguration.NConfigurationManager.GetEnvironment());
      }
      else if (!_validSwitches.Contains(args[0]))
      {
        PrintUsage();
        return 1;
      }
      else if (args[0].StartsWith("-a"))
      {
        if (args.Length < 2)
        {
          PrintUsage();
          return 1;
        }
        NConfiguration.NConfigurationManager.Initialize();
        var key = args[1];
                Console.WriteLine(NConfiguration.NConfigurationManager.GetAppSetting(key));
      }
      else if (args[0].StartsWith("-c"))
      {
        if (args.Length < 2)
        {
          PrintUsage();
          return 1;
        }
        NConfiguration.NConfigurationManager.Initialize();
        var key = args[1];
                Console.WriteLine(NConfiguration.NConfigurationManager.GetConnectionString(key));
      }
      else if (args[0].StartsWith("-l"))
      {
        if (args.Length >= 2)
        {
            PrintUsage();
            return 1;
        }
                Console.WriteLine("Key candidates for this PC");
                var candidates = NConfiguration.NConfigurationManager.GetKeyCandidates();
                foreach(var key in candidates) {
                    Console.WriteLine("\t - {0}", key);
                
                }
       }

      else if (args[0].StartsWith("-v"))
      {
        var validationResults = NConfiguration.NConfigurationManager.ValidateAllConfigurations();
        if (!validationResults.Any())
        {
          Console.WriteLine("All the configuration files are in sync");
          return 0;
        }

        using (var writer = new System.CodeDom.Compiler.IndentedTextWriter(Console.Error, "  "))
        {
          foreach (var validationResult in validationResults)
          {
            var color = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            writer.WriteLine("{0} has the following misconfigurations:", validationResult.Key);
            Console.ForegroundColor = color;
            writer.Indent = 1;
            foreach (var validationMessage in validationResult.Value)
            {
              writer.WriteLine(validationMessage);
            }
            writer.Indent = 0;
          }
        }
        return 1;
      }
      return 0;
    }

    private static void PrintUsage()
    {
      Console.Error.WriteLine("Usage: nconf [option] [key]");
      Console.Error.WriteLine("\t: -appSetting (-a) [KEY]: prints the appseting value for [KEY]");
      Console.Error.WriteLine("\t: -connectionString (-c) [KEY]: prints the connectionString value for [KEY]");
      Console.Error.WriteLine("\t: -validate (-v): validate all the environments");
      Console.Error.WriteLine("\t: -list (-l): list all the keys available to identify this pc");
    }
  }
}
