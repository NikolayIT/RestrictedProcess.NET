// <copyright file="CommandLine.cs" company="Nikolay Kostov (Nikolay.IT)">
// Copyright (c) Nikolay Kostov (Nikolay.IT). All Rights Reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>

namespace RestrictedProcess.Process
{
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Builds a command line that CommandLineToArgvW parses back into the exact arguments given.
    /// <para>
    /// Getting this wrong is not only a correctness problem. When CreateProcess is called without an
    /// application name it splits an unquoted command line on spaces and tries each prefix in turn, so an
    /// unquoted <c>C:\Program Files\app\run.exe</c> gives <c>C:\Program.exe</c> the first chance to run.
    /// The sandbox always passes the application name explicitly as well, but the command line still has
    /// to be well formed because that is what the child parses into its own argv.
    /// </para>
    /// </summary>
    internal static class CommandLine
    {
        /// <summary>
        /// Builds the full command line for a process, starting with the quoted executable path.
        /// </summary>
        /// <param name="fileName">The path of the executable, which becomes argv[0].</param>
        /// <param name="arguments">The arguments, each of which is escaped as a single argv entry.</param>
        /// <returns>The command line to pass to CreateProcessAsUser.</returns>
        public static string Build(string fileName, IEnumerable<string>? arguments)
        {
            var builder = new StringBuilder();
            AppendArgument(builder, fileName);

            if (arguments != null)
            {
                foreach (var argument in arguments)
                {
                    builder.Append(' ');
                    AppendArgument(builder, argument ?? string.Empty);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Appends a single argument, quoting and escaping it so it survives CommandLineToArgvW intact.
        /// </summary>
        /// <param name="builder">The command line being built.</param>
        /// <param name="argument">The argument to append.</param>
        public static void AppendArgument(StringBuilder builder, string argument)
        {
            if (argument.Length != 0 && !ContainsCharacterNeedingQuotes(argument))
            {
                builder.Append(argument);
                return;
            }

            builder.Append('"');

            for (var i = 0; i < argument.Length; i++)
            {
                var backslashes = 0;
                while (i < argument.Length && argument[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i == argument.Length)
                {
                    // Backslashes at the very end precede the closing quote, so they must be doubled to
                    // stop that quote from being escaped.
                    builder.Append('\\', backslashes * 2);
                    break;
                }

                if (argument[i] == '"')
                {
                    // Backslashes before a quote are doubled, and the quote itself is escaped.
                    builder.Append('\\', (backslashes * 2) + 1);
                    builder.Append('"');
                }
                else
                {
                    builder.Append('\\', backslashes);
                    builder.Append(argument[i]);
                }
            }

            builder.Append('"');
        }

        private static bool ContainsCharacterNeedingQuotes(string argument)
        {
            foreach (var character in argument)
            {
                if (character == ' ' || character == '\t' || character == '"')
                {
                    return true;
                }
            }

            return false;
        }
    }
}
