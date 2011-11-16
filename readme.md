#NConfigurationManager
Consolidate your different web, app.config's throughout your solution and over different machines. 

The goal is to create a transparent diffable overview of your settings across staging stages. No merging, no inheritence.

NConfigurationManager also throws helpful exceptions when the configured environment loads with more or less settings then the default
this will prevent annoying run time bugs when you forget to set that new connectionstring/appsetting.

##Setup
Nothing fancy just yet, this is an alpha release so no nuget or anything. 

1. Reference NConfigurationManager.dll and System.Configuration.dll
2. Create a folder called `NConfig.Environments` in a shared parent
3. Create a file called `environments.config` in previous folder with the following contents

```XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <appSettings>
    <add key="default" value="development" />
    <add key="COMPUTERNAME" value="development" />
	  <add key="COMPUTERNAME" value="test" />
	  <add key="FQDN" value="acceptance" />
	  <add key="IP" value="production" />
  </appSettings>
</configuration>
```

4. Create the environment config files (in this case test.config, development.config, acceptance.config, production.config)
i.e test.config:

```XML
<configuration>
  <appSettings>
    <clear/>
    <add key="environment" value="test" />
    <add key="key1" value="value1" />
  </appSettings>
  <connectionStrings>
    <clear/>
  </connectionStrings>
</configuration>
```

5. Now in your application's main /web app start call `NConfigurationManager.Initialize()` this will resolve which environment to pick up based on this order:
  FQDN, ComputerName, Domain (network domain), IPv4 and IPv6 addresses, "default"

6. Use `ConfigurationManager.AppSettings` and `ConfigurationManager.ConnectionStrings` like nothing happened.
