using System;
using System.Collections.Generic;

namespace CreateWallElevation
{
    internal sealed class ViewBatchFailure
    {
        public int Number { get; }
        public string Reason { get; }

        public ViewBatchFailure(int number, string reason)
        {
            Number = number;
            Reason = reason;
        }
    }

    internal sealed class ViewBatchResult<TView>
    {
        public readonly List<TView> Views = new List<TView>();
        public readonly List<ViewBatchFailure> Failures = new List<ViewBatchFailure>();
    }

    internal static class ViewBatch
    {
        // A returned view is tentative until its containing Revit transaction commits.
        public static ViewBatchResult<TView> Create<TSource, TView>(IList<TSource> sources,
            Func<TSource, int, TView> createAndCommitView, Func<Exception, bool> canSkip)
        {
            var result = new ViewBatchResult<TView>();
            for (int index = 0; index < sources.Count; index++)
            {
                try
                {
                    result.Views.Add(createAndCommitView(sources[index], index + 1));
                }
                catch (Exception error) when (canSkip(error))
                {
                    result.Failures.Add(new ViewBatchFailure(index + 1, error.Message));
                }
            }
            return result;
        }
    }
}
