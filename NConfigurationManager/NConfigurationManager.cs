using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.IO;
using System.Configuration;
using System.Net.NetworkInformation;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Web;
using System.Web.Configuration;
using System.Diagnostics;

namespace NConfiguration
{
    public static class NConfigurationManager
    {
        private static object _lock = new object();
        private static ReaderWriterLock _setEnvironmentLock = new ReaderWriterLock();
        private static ReaderWriterLock _readWriteLock = new ReaderWriterLock();
        private static string _rootDirectory;
        private static string _environmentsFile;
        private static FileSystemWatcher _watcher;
        private static bool Initialized = false;
        private static string _defaultEnvironment = null;
        private static string _environment = null;
        private static Configuration _currentConfig = null;

        static NConfigurationManager()
        {
            lock (_lock)
            {
                InitializeIntern();
            }
        }

		public static string RootDirectory { get { return _rootDirectory;  } }

        private static void OnChanged(object sender, FileSystemEventArgs e)
        {
            Refresh();
        }
        private static void OnRenamed(object sender, RenamedEventArgs e)
        {
            Refresh();
        }
        public static void Initialize()
        {
            Refresh();
        }
        /// <summary>
        /// Fall back to the given environment name as suppose to whatever "default" is pointing to.
        /// </summary>
        /// <param name="defaultEnvronment"></param>
        public static void Initialize(string defaultEnvironment)
        {
          _defaultEnvironment = defaultEnvironment;
            Refresh();
        }
        public static string GetEnvironment()
        {
          var e = string.Empty;
          _setEnvironmentLock.AcquireReaderLock(1000);
          e = _environment;
          _setEnvironmentLock.ReleaseReaderLock();
          return e;
        }

        private static void FindRootDirectory()
        {
          var path = System.AppDomain.CurrentDomain.BaseDirectory;
          while (
              (!Directory.Exists(Path.Combine(path, "nconfig.environments"))
              && !File.Exists(Path.Combine(path, ".nconfig")))
              && Directory.GetDirectoryRoot(path) != path
          )
          {
            path = Directory.GetParent(path).FullName;
          }
          if (File.Exists(Path.Combine(path, ".nconfig")))
          {
            var redirPath = File.ReadAllText(Path.Combine(path, ".nconfig"));
            if (string.IsNullOrEmpty(redirPath))
            {
              throw new ApplicationException(string.Format("Found a .config redirect file at {0} but it was empty", path));
            }
            redirPath = Path.GetFullPath(Path.Combine(path, redirPath));
            if (!Directory.Exists(redirPath))
            {
              throw new ApplicationException(string.Format("Found a .config redirect file at {0} it redirects to non existing folder {1}", path, redirPath));
            }
            _rootDirectory = redirPath;
            return;
          }

          if (Directory.GetDirectoryRoot(path) == path)
            throw new ApplicationException("Could not locate parent NConfig.Environments folder");
          _rootDirectory = Path.Combine(path, "nconfig.environments");
        }


        private static void InitializeIntern()
        {

            FindRootDirectory(); 
            _environmentsFile = Path.Combine(_rootDirectory, "environments.config");
            if (!File.Exists(_environmentsFile))
              throw new ApplicationException("Could not locate environments.config file in the NConfig.Environments folder: " + _environmentsFile);
            _watcher = new FileSystemWatcher(_rootDirectory, "*.config");

            _watcher.NotifyFilter = NotifyFilters.LastWrite
                | NotifyFilters.Size
                | NotifyFilters.Security
                | NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.FileName
                | NotifyFilters.Attributes;

            _watcher.Changed += new FileSystemEventHandler(OnChanged);
            _watcher.Created += new FileSystemEventHandler(OnChanged);
            _watcher.Deleted += new FileSystemEventHandler(OnChanged);
            _watcher.Renamed += new RenamedEventHandler(OnRenamed);
            _watcher.EnableRaisingEvents = true;
            Initialized = true;
        }

        /// <summary>
        /// Mono has a bug so on mono projects GetAppSetting is mandatory
        /// Gets the app setting.  
        /// https://bugzilla.novell.com/show_bug.cgi?id=545588
        /// </summary>
        /// <returns>The app setting.</returns>
        /// <param name="key">Key.</param>
        public static string GetAppSetting(string key) {
            if (_currentConfig == null)
                return null;

            var s = _currentConfig.AppSettings.Settings[key];
            return s != null ? s.Value : string.Empty;
        }
        /// <summary>
        /// Mono has a bug so on mono projects GetConnectionString is mandatory
        /// https://bugzilla.novell.com/show_bug.cgi?id=545588
        /// </summary>
        /// <returns>The app setting.</returns>
        /// <param name="key">Key.</param>
        public static string GetConnectionString(string key) {
            if (_currentConfig == null)
                return null;

            var s = _currentConfig.ConnectionStrings.ConnectionStrings[key];
            return s != null ? s.ConnectionString : string.Empty;
        }

        private static void Refresh()
        {
            lock (_lock)
            {
                if (!Initialized)
                    InitializeIntern();
                if (!Initialized)
                    throw new ApplicationException("An unknow error occured while trying to initialize");
                
                var environmentConfiguration = GetCurrentEnvironmentConfiguration();
                _currentConfig = environmentConfiguration;
                var path = AppDomain.CurrentDomain.GetData("APP_CONFIG_FILE").ToString();
                Configuration activeConfiguration = null;
                if (path.EndsWith("web.config"))
                    activeConfiguration = WebConfigurationManager.OpenWebConfiguration("~");
                else
                    activeConfiguration = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                try
                {
                    _readWriteLock.AcquireWriterLock(1000);
                    var equalAppSettings = HasEqualAppSettingsAndValues(activeConfiguration, environmentConfiguration);
                    var equalConnectionStrings = HasEqualConnectionStringsAndValues(activeConfiguration, environmentConfiguration);
                    if (!equalAppSettings)
                    { 
                        activeConfiguration.AppSettings.Settings.Clear();
                        foreach (KeyValueConfigurationElement a in environmentConfiguration.AppSettings.Settings)
                        {
                            activeConfiguration.AppSettings.Settings.Add(a.Key, a.Value);
                        }
                    }
                    if (!equalConnectionStrings)
                    { 
                        activeConfiguration.ConnectionStrings.ConnectionStrings.Clear();
                        foreach (ConnectionStringSettings s in environmentConfiguration.ConnectionStrings.ConnectionStrings)
                        {
                            activeConfiguration.ConnectionStrings.ConnectionStrings
                                .Add(new ConnectionStringSettings(s.Name, s.ConnectionString, s.ProviderName));
                        }
                    }
                    //we are checking for equality to prevent .Save causing an endless recursion.
                    if (!equalAppSettings || !equalConnectionStrings)
                    { 
                        activeConfiguration.Save(ConfigurationSaveMode.Full);
                        ConfigurationManager.RefreshSection("appSettings");
                        ConfigurationManager.RefreshSection("connectionStrings");

                    }
                }
                catch (Exception e)
                {
                    throw new ApplicationException("Could not copy over configuration", e);
                }
                finally
                {
                    _readWriteLock.ReleaseWriterLock();
                }
            }
        }
        
        public static void Switch(string environment)
        {
            
        }

        /// <summary>
        /// Validates all the environment configuration files
        /// </summary>
        /// <returns>A dictionary of environment name as key with a list of configuration error messages</returns>
        public static Dictionary<string, IList<string>> ValidateAllConfigurations()
        {
          var validationErrors = new Dictionary<string, IList<string>>();

          var formatExtraKey = "{0}.config has extra setting {1} not in {2}.config";
          var formatMissingKey = "{0}.config misses setting {1} from {2}.config";

          var formatExtraConnectionString = "{0}.config has extra connection {1} not in {2}.config";
          var formatMissingConnectionString = "{0}.config misses connection {1} from {2}.config";

          var environmentsConfiguration = OpenConfiguration(_environmentsFile);
          var settings = environmentsConfiguration.AppSettings.Settings;
          
          var defEnv = _defaultEnvironment ?? settings["default"].Value;
          var defaultConfiguration = OpenConfiguration(Path.Combine(_rootDirectory, defEnv + ".config"));

          var defaultConnections = new List<string>();
          foreach (ConnectionStringSettings k in defaultConfiguration.ConnectionStrings.ConnectionStrings)
            defaultConnections.Add(k.Name);


          var environments = settings.AllKeys.Select(k => settings[k].Value).Distinct();
          foreach (var environment in environments)
          {
            var environmentConfiguration = OpenConfiguration(Path.Combine(_rootDirectory, environment + ".config"));
            var equalAppSettings = HasEqualAppSettings(defaultConfiguration, environmentConfiguration);
            var equalConnectionStrings = HasEqualAppSettings(defaultConfiguration, environmentConfiguration);
            if (equalAppSettings && equalConnectionStrings)
              continue;

            var errors = new List<string>();
            if (!equalAppSettings)
            {
              
              foreach (var s in environmentConfiguration.AppSettings.Settings.AllKeys)
                if (!defaultConfiguration.AppSettings.Settings.AllKeys.Contains(s))
                  errors.Add(string.Format(formatExtraKey, environment, s, defEnv));

              foreach (var s in defaultConfiguration.AppSettings.Settings.AllKeys)
                if (!environmentConfiguration.AppSettings.Settings.AllKeys.Contains(s))
                  errors.Add(string.Format(formatMissingKey, environment, s, defEnv));
            }
            if (!equalConnectionStrings)
            {
              var environmentConnections = new List<string>();
              foreach (ConnectionStringSettings k in environmentConfiguration.ConnectionStrings.ConnectionStrings)
                environmentConnections.Add(k.Name);
             
              foreach (var s in environmentConnections)
                if (!defaultConnections.Contains(s))
                  errors.Add(string.Format(formatExtraConnectionString, environment, s, defEnv));

              foreach (var s in defaultConnections)
                if (!environmentConnections.Contains(s))
                  errors.Add(string.Format(formatMissingConnectionString, environment, s, defEnv));
            }
            validationErrors.Add(environment, errors);

          }
          return validationErrors;
        }
           
        public static IEnumerable<string> GetKeyCandidates()
        {
            string host = null;
            var keys = new List<string>();
            try {
                var ipProperties = IPGlobalProperties.GetIPGlobalProperties();
                var fqdn = string.Format("{0}.{1}", ipProperties.HostName, ipProperties.DomainName);
                host = ipProperties.HostName;
                var domain = ipProperties.DomainName;
                keys.Add(fqdn);
                keys.Add(host);
                keys.Add(host);
            }
            catch {
                host = System.Environment.MachineName;
                keys.Add(host);
            }
            try
            {
            var ipEntry = Dns.GetHostEntry(host);
            var keyCandidates = keys.
                Concat(ipEntry.AddressList.
                       Where(a => a.AddressFamily == AddressFamily.InterNetwork || a.AddressFamily == AddressFamily.InterNetworkV6)
                       .Select(a => a.ToString())
                ).Select(a => a.ToString().ToLowerInvariant());
            return keyCandidates;
            }
            catch { return keys; }
        }
    
        private static Configuration GetCurrentEnvironmentConfiguration()
        {
            var environmentsConfiguration = OpenConfiguration(_environmentsFile);
            var settings = environmentsConfiguration.AppSettings.Settings;
            var keys = environmentsConfiguration.AppSettings.Settings.AllKeys.ToDictionary(k=>k.ToLowerInvariant(), v=>v);

            var keyCandidates = GetKeyCandidates();

            var defEnv = _defaultEnvironment ?? settings["default"].Value;
            var environment = defEnv;
            foreach (var key in keyCandidates)
            {
              string environmentKey = string.Empty;
              if (!keys.TryGetValue(key, out environmentKey))
                continue;

              environment = settings[environmentKey].Value;
              break;
            }
            if (environment != defEnv)
            {
                var defaultConfiguration = OpenConfiguration(Path.Combine(_rootDirectory, defEnv + ".config"));
                var environmentConfiguration = OpenConfiguration(Path.Combine(_rootDirectory, environment + ".config"));

                var equalAppSettings = HasEqualAppSettings(defaultConfiguration, environmentConfiguration);
                if (!equalAppSettings)
                    throw new ApplicationException("Could not load environment: '" + environment + "' it's appSetting misses or has extra keys");

                var equalConnectionStrings = HasEqualConnectionStrings(defaultConfiguration, environmentConfiguration);
                if (!equalConnectionStrings)
                    throw new ApplicationException("Could not load environment: '" + environment + "' it's connectionStrings misses or has extra connections");
            }

            _setEnvironmentLock.AcquireWriterLock(1000);
            _environment = environment;
            _setEnvironmentLock.ReleaseWriterLock();

            return OpenConfiguration(Path.Combine(_rootDirectory, environment + ".config"));
        }

        private static bool HasEqualAppSettings(Configuration configuration, Configuration compareConfiguration)
        {
            var configSettings = configuration.AppSettings.Settings.AllKeys;
            var compareSettings = compareConfiguration.AppSettings.Settings.AllKeys;
            var equalAppSettings = configSettings.Count() == compareSettings.Count()
                        && configSettings.Except(compareSettings).Count() == 0;
            return equalAppSettings;
        }

        private static bool HasEqualConnectionStrings(Configuration configuration, Configuration compareConfiguration)
        {
            var configConnections = configuration.ConnectionStrings.ConnectionStrings;
            var compareConnections = compareConfiguration.ConnectionStrings.ConnectionStrings;
            var configConnectionKeys = new List<string>();
            foreach (ConnectionStringSettings k in configConnections)
                configConnectionKeys.Add(k.Name);

            var compareConnectionKeys = new List<string>();
            foreach (ConnectionStringSettings k in compareConnections)
                compareConnectionKeys.Add(k.Name);

            var equalConnectionStrings = configConnections.Count == compareConnections.Count
                && configConnectionKeys.Except(compareConnectionKeys).Count() == 0;

            return equalConnectionStrings;
        }

        private static bool HasEqualAppSettingsAndValues(Configuration configuration, Configuration compareConfiguration)
        {
            if (!HasEqualAppSettings(configuration, compareConfiguration))
                return false;

            var configSettings = configuration.AppSettings.Settings;
            var compareSettings = compareConfiguration.AppSettings.Settings;
            foreach (KeyValueConfigurationElement setting in configSettings)
            {
                if (setting.Value != compareSettings[setting.Key].Value)
                    return false;
            }
            return true;
        }
 
        private static bool HasEqualConnectionStringsAndValues(Configuration configuration, Configuration compareConfiguration)
        {
            if (!HasEqualConnectionStrings(configuration, compareConfiguration))
                return false;

            var configConnections = configuration.ConnectionStrings.ConnectionStrings;
            var compareConnections = compareConfiguration.ConnectionStrings.ConnectionStrings;
            foreach (ConnectionStringSettings k in configConnections)
            {
                if (k.ConnectionString != compareConnections[k.Name].ConnectionString
                    || k.ProviderName != compareConnections[k.Name].ProviderName)
                    return false;
            }
            return true;
        }


        private static Configuration OpenConfiguration(string path)
        {
            ExeConfigurationFileMap configMap = new ExeConfigurationFileMap();
            configMap.ExeConfigFilename = path;
            var configuration = ConfigurationManager.OpenMappedExeConfiguration(configMap, ConfigurationUserLevel.None);
            return configuration;
        }
    }
}
