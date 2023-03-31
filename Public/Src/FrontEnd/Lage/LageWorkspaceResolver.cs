// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.FrontEnd.Lage.ProjectGraph;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.Lage
{
    /// <summary>
    /// Workspace resolver for Lage
    /// </summary>
    public class LageWorkspaceResolver : ToolBasedJavaScriptWorkspaceResolver<LageConfiguration, ILageResolverSettings>
    {
        /// <summary>
        /// CODESYNC: the BuildXL deployment spec that places the tool
        /// </summary>
        protected override RelativePath RelativePathToGraphConstructionTool => RelativePath.Create(Context.StringTable, @"tools\LageGraphBuilder\main.js");

        /// <summary>
        /// Lage provides its own execution semantics
        /// </summary>
        protected override bool ApplyBxlExecutionSemantics() => false;

        /// <inheritdoc/>
        public LageWorkspaceResolver() : base(KnownResolverKind.LageResolverKind)
        {
        }

        /// <inheritdoc/>
        protected override bool TryFindGraphBuilderToolLocation(ILageResolverSettings resolverSettings, BuildParameters.IBuildParameters buildParameters, out AbsolutePath npmLocation, out string failure)
        {
            // If the base location was provided at configuration time, we honor it as is
            if (resolverSettings.NpmLocation.HasValue)
            {
                npmLocation = resolverSettings.NpmLocation.Value.Path;
                failure = string.Empty;
                return true;
            }

            // If the location was not provided, let's try to see if NPM is under %PATH%
            string paths = buildParameters["PATH"];

            if (!FrontEndUtilities.TryFindToolInPath(Context, Host, paths, new[] { "npm", "npm.cmd" }, out npmLocation))
            {
                failure = "A location for 'npm' is not explicitly specified. However, 'npm' doesn't seem to be part of PATH. You can either specify the location explicitly using 'npmLocation' field in " +
                    $"the Lage resolver configuration, or make sure 'npm' is part of your PATH. Current PATH is '{paths}'.";
                return false;
            }

            failure = string.Empty;

            // Just verbose log this
            Tracing.Logger.Log.UsingNpmAt(Context.LoggingContext, resolverSettings.Location(Context.PathTable), npmLocation.ToString(Context.PathTable));

            return true;
        }

        /// <summary>
        /// The graph construction tool expects: path-to-repo-root path-to-output-graph path-to-npm commands-to-execute
        /// </summary>
        protected override string GetGraphConstructionToolArguments(AbsolutePath outputFile, AbsolutePath toolLocation, AbsolutePath bxlGraphConstructionToolPath, string nodeExeLocation)
        {
            // Node.exe sometimes misinterprets backslashes (e.g. "C:\" is interpreted such that \ is escaping quotes)
            // Use forward slashes for all node.exe arguments to avoid this.
            string pathToRepoRoot = ResolverSettings.Root.ToString(Context.PathTable, PathFormat.Script);

            // Get the list of all regular commands
            IEnumerable<string> commands = ComputedCommands.Keys.Where(command => !CommandGroups.ContainsKey(command)).Union(CommandGroups.Values.SelectMany(commandMembers => commandMembers)).ToList();
            
            // Pass the 6th argument (lage location) as "undefined" string. This argument is used by Office implementation.
            var args = $@"""{nodeExeLocation}"" ""{bxlGraphConstructionToolPath.ToString(Context.PathTable, PathFormat.Script)}"" ""{pathToRepoRoot}"" ""{outputFile.ToString(Context.PathTable, PathFormat.Script)}"" ""{toolLocation.ToString(Context.PathTable, PathFormat.Script)}"" ""{string.Join(" ", commands)}"" ""undefined""";
            
            return JavaScriptUtilities.GetCmdArguments(args);
        }

        /// <inheritdoc/>
        protected override string GetProjectNameForGroup(IReadOnlyCollection<JavaScriptProject> groupMembers, string groupCommandName)
        {
            Contract.Requires(groupMembers.Count > 0);
            var firstMember = groupMembers.First();

            // Lage project names look like project-name#script-command. All members in the same group are
            // supposed to share the same project name, so just use the first member
            string name = firstMember.Name;
            var index = name.LastIndexOf('#');
            // There should always be a '#' in the name, but just be defensive here
            if (index >= 0)
            {
                name = name[..(index + 1)];
            }

            // Let's keep the same Lage nomenclature for the group
            return $"{name}{groupCommandName}";
        }
    }
}
