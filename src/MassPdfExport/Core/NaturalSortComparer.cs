using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GvrTools.MassPdfExport.Core
{
    /// <summary>
    /// Sorts strings the way a human expects (A-2, A-10, A-100 instead of A-10, A-100, A-2),
    /// which matters for Revit sheet numbers that mix letters and digits.
    /// </summary>
    public sealed class NaturalSortComparer : IComparer<string>
    {
        public static readonly NaturalSortComparer Instance = new NaturalSortComparer();

        private static readonly Regex ChunkPattern = new Regex(@"\d+|\D+", RegexOptions.Compiled);

        public int Compare(string x, string y)
        {
            if (x == y) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var xChunks = ChunkPattern.Matches(x);
            var yChunks = ChunkPattern.Matches(y);
            int count = Math.Min(xChunks.Count, yChunks.Count);

            for (int i = 0; i < count; i++)
            {
                string xChunk = xChunks[i].Value;
                string yChunk = yChunks[i].Value;

                bool isNumeric = char.IsDigit(xChunk[0]) && char.IsDigit(yChunk[0]);
                int comparison;

                if (isNumeric)
                {
                    // Compare as numbers, falling back to length/text for very long digit runs.
                    if (xChunk.TrimStart('0').Length != yChunk.TrimStart('0').Length)
                        comparison = xChunk.TrimStart('0').Length.CompareTo(yChunk.TrimStart('0').Length);
                    else
                        comparison = string.CompareOrdinal(xChunk, yChunk);
                }
                else
                {
                    comparison = string.Compare(xChunk, yChunk, StringComparison.CurrentCultureIgnoreCase);
                }

                if (comparison != 0) return comparison;
            }

            return xChunks.Count.CompareTo(yChunks.Count);
        }
    }
}
