﻿using System;
using System.Reflection;
using System.Text.RegularExpressions;
using MailMergeLib.SmartFormatMail.Core.Extensions;
using MailMergeLib.SmartFormatMail.Core.Parsing;

namespace MailMergeLib.SmartFormatMail.Extensions
{
    public class ConditionalFormatter : IFormatter
    {
        public string[] Names { get; set; } = { "conditional", "cond", "" };

        private static readonly Regex _complexConditionPattern
            = new Regex(@"^  (?:   ([&/]?)   ([<>=!]=?)   ([0-9.-]+)   )+   \?",
            //   Description:      and/or    comparator     value
            RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);

        public bool TryEvaluateFormat(IFormattingInfo formattingInfo)
        {
            var format = formattingInfo.Format;
            var current = formattingInfo.CurrentValue;
            
            if (format == null) return false;
            // Ignore a leading ":", which is used to bypass the PluralLocalizationExtension
            if (format.baseString[format.startIndex] == ':')
            {
                format = format.Substring(1);
            }

            // See if the format string contains un-nested "|":
            var parameters = format.Split('|');
            if (parameters.Count == 1) return false; // There are no parameters found.

            // See if the value is a number:
            var currentIsNumber =
                current is byte || current is short || current is int || current is long
                || current is float || current is double || current is decimal;
            // An Enum is a number too:
#if NETSTANDARD1_6
            if (currentIsNumber == false && current != null && current.GetType().GetTypeInfo().IsEnum)
#else
            if (currentIsNumber == false && current != null && current.GetType().IsEnum)
#endif
            {
                currentIsNumber = true;
            }
            var currentNumber = currentIsNumber ? Convert.ToDecimal(current) : 0;
            
            int paramIndex; // Determines which parameter to use for output

            // First, we'll see if we are using "complex conditions":
            if (currentIsNumber) {
                paramIndex = -1;
                while (true)
                {
                    paramIndex++;
                    if (paramIndex == parameters.Count)
                    {
                        // We reached the end of our parameters,
                        // so we output nothing
                        return true;
                    }
                    bool conditionWasTrue;
                    Format outputItem;
                    if (!TryEvaluateCondition(parameters[paramIndex], currentNumber, out conditionWasTrue, out outputItem))
                    {
                        // This parameter doesn't have a
                        // complex condition (making it a "else" condition)

                        // Only do "complex conditions" if the first item IS a "complex condition".
                        if (paramIndex == 0)
                        {
                            break;
                        }
                        // Otherwise, output the "else" section:
                        conditionWasTrue = true;
                    }

                    // If the conditional statement was true, then we can break.
                    if (conditionWasTrue)
                    {
                        formattingInfo.Write(outputItem, current);
                        return true;
                    }
                }
                // We don't have any "complex conditions",
                // so let's do the normal conditional formatting:
            }


            var paramCount = parameters.Count;

            // Determine the Current item's Type:
            if (currentIsNumber) {
                if (currentNumber < 0)
                {
                    paramIndex = paramCount - 1;
                }
                else
                {
                    paramIndex = Math.Min((int)Math.Floor(currentNumber), paramCount - 1);
                }
            }
            else if (current is bool) {
                // Bool: True|False
                bool arg = (bool)current;
                if (arg)
                {
                    paramIndex = 0;
                }
                else
                {
                    paramIndex = 1;
                }
            }
            else if (current is DateTime) {
                // Date: Past|Present|Future   or   Past/Present|Future
                DateTime arg = (DateTime)current;
                if (paramCount == 3 && arg.Date == DateTime.Today)
                {
                    paramIndex = 1;
                }
                else if (arg <= DateTime.Now)
                {
                    paramIndex = 0;
                }
                else
                {
                    paramIndex = paramCount - 1;
                }
            }
            else if (current is TimeSpan) {
                // TimeSpan: Negative|Zero|Positive  or  Negative/Zero|Positive
                TimeSpan arg = (TimeSpan)current;
                if (paramCount == 3 && arg == TimeSpan.Zero)
                {
                    paramIndex = 1;
                }
                else if (arg.CompareTo(TimeSpan.Zero) <= 0)
                {
                    paramIndex = 0;
                }
                else
                {
                    paramIndex = paramCount - 1;
                }
            }
            else if (current is string) {
                // String: Value|NullOrEmpty
                var arg = (string)current;
                if (!string.IsNullOrEmpty(arg))
                {
                    paramIndex = 0;
                }
                else
                {
                    paramIndex = 1;
                }
            } else {
                // Object: Something|Nothing
                object arg = current;
                if (arg != null)
                {
                    paramIndex = 0;
                }
                else
                {
                    paramIndex = 1;
                }
            }

            // Now, output the selected parameter:
            var selectedParameter = parameters[paramIndex];

            // Output the selectedParameter:
            formattingInfo.Write(selectedParameter, current);
            return true;
        }

        /// <summary>
        /// Evaluates a conditional format.
        ///
        /// Each condition must start with a comparor: "&gt;/&gt;=", "&lt;/&lt;=", "=", "!=".
        /// Conditions must be separated by either "&amp;" (AND) or "/" (OR).
        /// The conditional statement must end with a "?".
        ///
        /// Examples:
        /// &gt;=21&amp;&lt;30&amp;!=25/=40?
        /// </summary>
        private static bool TryEvaluateCondition(Format parameter, decimal value, out bool conditionResult, out Format outputItem)
        {
            conditionResult = false;
            // Let's evaluate the conditions into a boolean value:
            Match m = _complexConditionPattern.Match(parameter.baseString, parameter.startIndex, parameter.endIndex - parameter.startIndex);
            if (!m.Success) {
                // Could not parse the "complex condition"
                outputItem = parameter;
                return false;
            }


            CaptureCollection andOrs = m.Groups[1].Captures;
            CaptureCollection comps = m.Groups[2].Captures;
            CaptureCollection values = m.Groups[3].Captures;

            for (int i = 0; i < andOrs.Count; i++) {
                decimal v = decimal.Parse(values[i].Value);
                bool exp = false;
                switch (comps[i].Value) {
                    case ">":
                        exp = value > v;
                        break;
                    case "<":
                        exp = value < v;
                        break;
                    case "=":
                    case "==":
                        exp = value == v;
                        break;
                    case "<=":
                        exp = value <= v;
                        break;
                    case ">=":
                        exp = value >= v;
                        break;
                    case "!":
                    case "!=":
                        exp = value != v;
                        break;
                }

                if (i == 0) {
                    conditionResult = exp;
                }
                else if (andOrs[i].Value == "/") {
                    conditionResult = conditionResult | exp;
                }
                else {
                    conditionResult = conditionResult & exp;
                }
            }

            // Successful
            // Output the substring that doesn't contain the "complex condition"
            var newStartIndex = m.Index + m.Length - parameter.startIndex;
            outputItem = parameter.Substring(newStartIndex);
            return true;
        }

    }
}