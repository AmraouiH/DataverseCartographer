using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DataverseCartographer.Models;

namespace DataverseCartographer.Services.Analysis
{
    /// <summary>
    /// Static analysis of a JavaScript web resource: Dataverse calls and the table they target.
    /// Detects Xrm.WebApi (createRecord, updateRecord, deleteRecord, retrieveRecord, retrieveMultipleRecords, also through an alias),
    /// Web API URLs (/api/data/v9.x/accounts with the HTTP verb found nearby), FetchXML, form navigation
    /// (openForm, openEntityForm, navigateTo, lookupObjects) and custom API / action calls (operationName).
    /// Each call is attached to the function that contains it. Heuristic: names held in variables are not resolved.
    /// </summary>
    public static class ScriptAnalyzer
    {
        private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;

        private static readonly Regex WebApi = new Regex(
            @"\b(createRecord|updateRecord|deleteRecord|retrieveRecord|retrieveMultipleRecords)\s*\(\s*([""'`])([A-Za-z0-9_]+)\2", Options);

        private static readonly Regex WebApiUrl = new Regex(@"/api/data/v\d+(?:\.\d+)?/([A-Za-z0-9_]+)", Options);
        private static readonly Regex HttpVerb = new Regex(@"[""'`](GET|POST|PATCH|PUT|DELETE)[""'`]", Options | RegexOptions.IgnoreCase);
        private static readonly Regex Fetch = new Regex(@"<(?:link-)?entity\b[^>]*?\bname\s*=\s*\\?[""']([A-Za-z0-9_]+)\\?[""']", Options | RegexOptions.IgnoreCase);
        private static readonly Regex OpenForm = new Regex(@"\b(openForm|navigateTo|lookupObjects)\s*\(\s*\{[^}]{0,400}?\b(?:entityName|entityTypes)\s*:\s*\[?\s*[""'`]([A-Za-z0-9_]+)", Options);
        private static readonly Regex OpenEntityForm = new Regex(@"\bopenEntityForm\s*\(\s*[""'`]([A-Za-z0-9_]+)", Options);
        private static readonly Regex OperationName = new Regex(@"\boperationName\s*:\s*[""'`]([A-Za-z0-9_.]+)", Options);

        private static readonly Regex[] Declarations =
        {
            new Regex(@"\bfunction\s+([A-Za-z_$][\w$]*)\s*\(", Options),
            new Regex(@"([A-Za-z_$][\w$]*)\s*[:=]\s*(?:async\s+)?function\b", Options),
            new Regex(@"([A-Za-z_$][\w$]*)\s*[:=]\s*(?:async\s*)?(?:\([^()]*\)|[A-Za-z_$][\w$]*)\s*=>", Options),
            new Regex(@"^\s*(?:async\s+)?([A-Za-z_$][\w$]*)\s*\([^()]*\)\s*\{", Options | RegexOptions.Multiline)
        };

        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "if", "for", "while", "switch", "catch", "function", "return", "with", "else", "do", "try", "new", "typeof"
        };

        /// <param name="tables">Logical names of the tables of the organization.</param>
        /// <param name="entitySets">Table logical name by entity set name (« accounts » → account).</param>
        public static List<ScriptCall> Analyze(string script, ICollection<string> tables, IDictionary<string, string> entitySets)
        {
            var calls = new List<ScriptCall>();
            if (string.IsNullOrEmpty(script)) return calls;

            var code = BlankComments(script);
            var lineStarts = LineStarts(code);
            var functions = FunctionPositions(code);

            void Add(int index, string operation, string entity, bool read)
            {
                if (entity != null && !tables.Contains(entity)) entity = null;
                calls.Add(new ScriptCall
                {
                    Operation = operation,
                    Entity = entity?.ToLowerInvariant(),
                    Function = FunctionAt(functions, index),
                    Line = LineAt(lineStarts, index),
                    IsRead = read
                });
            }

            foreach (Match match in WebApi.Matches(code))
            {
                switch (match.Groups[1].Value)
                {
                    case "createRecord": Add(match.Index, "Create", match.Groups[3].Value, false); break;
                    case "updateRecord": Add(match.Index, "Update", match.Groups[3].Value, false); break;
                    case "deleteRecord": Add(match.Index, "Delete", match.Groups[3].Value, false); break;
                    case "retrieveRecord": Add(match.Index, "Retrieve", match.Groups[3].Value, true); break;
                    default: Add(match.Index, "RetrieveMultiple", match.Groups[3].Value, true); break;
                }
            }

            foreach (Match match in WebApiUrl.Matches(code))
            {
                var set = match.Groups[1].Value;
                if (set.Equals("$batch", StringComparison.OrdinalIgnoreCase)) continue;
                if (!entitySets.TryGetValue(set, out var table))
                {
                    // Not a table: unbound action / function (WhoAmI, new_MyAction…)
                    Add(match.Index, "Execute " + set, null, false);
                    continue;
                }
                var verb = NearbyVerb(code, match.Index);
                switch (verb)
                {
                    case "POST": Add(match.Index, "Create", table, false); break;
                    case "PATCH":
                    case "PUT": Add(match.Index, "Update", table, false); break;
                    case "DELETE": Add(match.Index, "Delete", table, false); break;
                    default: Add(match.Index, "Retrieve", table, true); break;
                }
            }

            foreach (Match match in Fetch.Matches(code)) Add(match.Index, "RetrieveMultiple", match.Groups[1].Value, true);
            foreach (Match match in OpenForm.Matches(code))
            {
                var operation = match.Groups[1].Value == "openForm" ? "OpenForm" : match.Groups[1].Value == "navigateTo" ? "Navigate" : "Lookup";
                Add(match.Index, operation, match.Groups[2].Value, true);
            }
            foreach (Match match in OpenEntityForm.Matches(code)) Add(match.Index, "OpenForm", match.Groups[1].Value, true);
            foreach (Match match in OperationName.Matches(code)) Add(match.Index, "Execute " + match.Groups[1].Value, null, false);

            return calls
                .GroupBy(c => (c.Operation, c.Entity ?? string.Empty, c.Function ?? string.Empty))
                .Select(g => g.OrderBy(c => c.Line).First())
                .OrderBy(c => c.IsRead)
                .ThenBy(c => c.Entity ?? "~")
                .ThenBy(c => c.Line)
                .ToList();
        }

        /// <summary>The HTTP verb written closest to a Web API URL (same request), GET by default.</summary>
        private static string NearbyVerb(string code, int index)
        {
            var start = Math.Max(0, index - 250);
            var end = Math.Min(code.Length, index + 250);
            string best = null;
            var distance = int.MaxValue;
            foreach (Match match in HttpVerb.Matches(code.Substring(start, end - start)))
            {
                var d = Math.Abs(start + match.Index - index);
                if (d >= distance) continue;
                distance = d;
                best = match.Groups[1].Value.ToUpperInvariant();
            }
            return best ?? "GET";
        }

        /// <summary>Comments replaced by spaces (line breaks kept, so that line numbers stay right). Strings are left untouched.</summary>
        private static string BlankComments(string code)
        {
            var result = new StringBuilder(code);
            var i = 0;
            char quote = '\0';
            while (i < code.Length)
            {
                var c = code[i];
                if (quote != '\0')
                {
                    if (c == '\\') i++;
                    else if (c == quote) quote = '\0';
                    i++;
                    continue;
                }
                if (c == '"' || c == '\'' || c == '`')
                {
                    quote = c;
                    i++;
                    continue;
                }
                if (c == '/' && i + 1 < code.Length && code[i + 1] == '/')
                {
                    while (i < code.Length && code[i] != '\n') result[i++] = ' ';
                    continue;
                }
                if (c == '/' && i + 1 < code.Length && code[i + 1] == '*')
                {
                    while (i < code.Length && !(code[i] == '*' && i + 1 < code.Length && code[i + 1] == '/'))
                    {
                        if (code[i] != '\n') result[i] = ' ';
                        i++;
                    }
                    if (i < code.Length) result[i++] = ' ';
                    if (i < code.Length) result[i++] = ' ';
                    continue;
                }
                i++;
            }
            return result.ToString();
        }

        private static List<int> LineStarts(string code)
        {
            var starts = new List<int> { 0 };
            for (var i = 0; i < code.Length; i++)
            {
                if (code[i] == '\n') starts.Add(i + 1);
            }
            return starts;
        }

        private static int LineAt(List<int> starts, int index)
        {
            var position = starts.BinarySearch(index);
            return (position >= 0 ? position : ~position - 1) + 1;
        }

        private static List<(int Index, string Name)> FunctionPositions(string code)
        {
            var result = new List<(int, string)>();
            foreach (var regex in Declarations)
            {
                foreach (Match match in regex.Matches(code))
                {
                    var name = match.Groups[1].Value;
                    if (!Keywords.Contains(name)) result.Add((match.Index, name));
                }
            }
            result.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return result;
        }

        /// <summary>The last function declared before the call (nesting is approximated, which is enough to locate the call).</summary>
        private static string FunctionAt(List<(int Index, string Name)> functions, int index)
        {
            string name = null;
            foreach (var function in functions)
            {
                if (function.Index > index) break;
                name = function.Name;
            }
            return name;
        }
    }
}
