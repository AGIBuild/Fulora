using System.CommandLine;
using Agibuild.Fulora.Cli.Commands;

var rootCommand = new RootCommand("Agibuild.Fulora CLI — scaffold, develop, and manage hybrid apps");

rootCommand.Subcommands.Add(NewCommand.Create());
rootCommand.Subcommands.Add(GenerateCommand.Create());
rootCommand.Subcommands.Add(DevCommand.Create());
rootCommand.Subcommands.Add(AddCommand.Create());
rootCommand.Subcommands.Add(ListPluginsCommand.Create());
rootCommand.Subcommands.Add(SearchCommand.Create());
rootCommand.Subcommands.Add(PackageCommand.Create());

var config = new CommandLineConfiguration(rootCommand);
var parseResult = config.Parse(args);
return await parseResult.InvokeAsync();
