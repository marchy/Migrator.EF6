﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.DotNet.Cli.Utils;
using NuGet.Frameworks;

namespace Microsoft.Extensions.Internal
{
	internal static class DotnetToolDispatcher
	{
		private const string DispatcherVersionArgumentName = "--dispatcher-version";

		private static readonly string DispatcherName = Assembly.GetEntryAssembly().GetName().Name;

		/// <summary>
		/// Invokes the 'tool' with the dependency context of the user's project.
		/// </summary>
		/// <param name="dependencyToolPath">The executable which needs to be invoked.</param>
		/// <param name="dispatchArgs">Arguments to pass to the executable.</param>
		/// <param name="framework"></param>
		/// <param name="configuration"></param>
		/// <param name="assemblyFullPath"></param>
		public static Command CreateDispatchCommand(
			string dependencyToolPath,
			IEnumerable<string> dispatchArgs,
			NuGetFramework framework,
			string configuration,
			string assemblyFullPath)
		{
			configuration = configuration ?? "Debug";
			Command command;
			var dispatcherVersionArgumentValue = ResolveDispatcherVersionArgumentValue(DispatcherName);
			var dispatchArgsList = new List<string>(dispatchArgs);
			dispatchArgsList.Add(DispatcherVersionArgumentName);
			dispatchArgsList.Add(dispatcherVersionArgumentValue);

			EnsureBindingRedirects(assemblyFullPath, Path.GetFileName(dependencyToolPath));
			// For Full framework, we can directly invoke the <dependencyTool>.exe from the user's bin folder.
			command = Command.Create(dependencyToolPath, dispatchArgsList);

			return command;
		}

		private static void EnsureBindingRedirects(string assemblyFullPath, string toolName)
		{
			// This is a temporary workaround.
			// `dotnet build` should generate the binding redirects for the project dependency tools as well.
			var bindingRedirectFile = $"{assemblyFullPath}.config";

			if (File.Exists(bindingRedirectFile))
			{
				var text = File.ReadAllText(bindingRedirectFile);
				var toolBindingRedirectFile = Path.Combine(Path.GetDirectoryName(assemblyFullPath), $"{toolName}.config");
				File.WriteAllText(toolBindingRedirectFile, text);
			}
		}

		public static bool IsDispatcher(string[] programArgs) =>
			!programArgs.Contains(DispatcherVersionArgumentName, StringComparer.OrdinalIgnoreCase);

		public static void EnsureValidDispatchRecipient(ref string[] programArgs) =>
			EnsureValidDispatchRecipient(ref programArgs, DispatcherName);

		public static void EnsureValidDispatchRecipient(ref string[] programArgs, string toolName)
		{
			if (!programArgs.Contains(DispatcherVersionArgumentName, StringComparer.OrdinalIgnoreCase))
			{
				return;
			}

			var dispatcherArgumentIndex = Array.FindIndex(
				programArgs,
				(value) => string.Equals(value, DispatcherVersionArgumentName, StringComparison.OrdinalIgnoreCase));
			var dispatcherArgumentValueIndex = dispatcherArgumentIndex + 1;
			if (dispatcherArgumentValueIndex < programArgs.Length)
			{
				var dispatcherVersion = programArgs[dispatcherArgumentValueIndex];

				var dispatcherVersionArgumentValue = ResolveDispatcherVersionArgumentValue(toolName);
				if (string.Equals(dispatcherVersion, dispatcherVersionArgumentValue, StringComparison.Ordinal))
				{
					// Remove dispatcher arguments from
					var preDispatcherArgument = programArgs.Take(dispatcherArgumentIndex);
					var postDispatcherArgument = programArgs.Skip(dispatcherArgumentIndex + 2);
					var newProgramArguments = preDispatcherArgument.Concat(postDispatcherArgument);
					programArgs = newProgramArguments.ToArray();
					return;
				}
			}

			// Could not validate the dispatchers version.
			throw new InvalidOperationException(
				$"Could not invoke tool {toolName}. Ensure it has matching versions in the project.json's 'dependencies' and 'tools' sections.");
		}

		// Internal for testing
		internal static string ResolveDispatcherVersionArgumentValue(string toolName)
		{
			var toolAssembly = Assembly.Load(new AssemblyName(toolName));

			var informationalVersionAttribute = toolAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

			Debug.Assert(informationalVersionAttribute != null);

			var informationalVersion = informationalVersionAttribute?.InformationalVersion ??
				toolAssembly.GetName().Version.ToString();

			return informationalVersion;
		}
	}
}
