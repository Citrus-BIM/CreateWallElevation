using System;
using System.Collections.Generic;
using CreateWallElevation;

internal static class ViewBatchTests
{
    public static void Register(Action<string, Action> test)
    {
        test("View batch keeps successful views around a failed middle boundary", () =>
        {
            var result = ViewBatch.Create(new[] { "first", "bad", "third" }, (source, number) =>
            {
                if (source == "bad") throw new ArgumentException("Cannot orient elevation");
                return source + "_" + number;
            }, error => error is ArgumentException);
            Require(result.Views.Count == 2 && result.Views[0] == "first_1" && result.Views[1] == "third_3",
                "A failed boundary must not discard other views or renumber later boundaries.");
            Require(result.Failures.Count == 1 && result.Failures[0].Number == 2 &&
                result.Failures[0].Reason == "Cannot orient elevation", "The skipped boundary and reason must be reported.");
        });
        test("View batch reports every failure when no boundary succeeds", () =>
        {
            var result = ViewBatch.Create<int, int>(new[] { 1, 2, 3 }, (source, number) =>
                throw new ArgumentException("Bad boundary " + number), error => error is ArgumentException);
            Require(result.Views.Count == 0 && result.Failures.Count == 3,
                "No successful views means the room transaction must not be counted as completed.");
            Require(result.Failures[2].Number == 3 && result.Failures[2].Reason == "Bad boundary 3",
                "All failed boundaries need their original numbers.");
        });
        test("View batch preserves all successful views and their order", () =>
        {
            var result = ViewBatch.Create(new[] { 8, 5, 2 }, (source, number) => source + number,
                error => error is ArgumentException);
            Require(result.Views.Count == 3 && result.Views[0] == 9 && result.Views[1] == 7 && result.Views[2] == 5 &&
                result.Failures.Count == 0, "Successful batches must remain unchanged.");
        });
        test("View batch propagates fatal failures without attempting later views", () =>
        {
            var fatal = new ApplicationException("Document regeneration failed");
            var attempted = new List<int>();
            try
            {
                ViewBatch.Create(new[] { 1, 2, 3 }, (source, number) =>
                {
                    attempted.Add(number);
                    if (number == 2) throw fatal;
                    return source;
                }, error => error is ArgumentException);
                throw new Exception("A fatal exception must escape the batch.");
            }
            catch (ApplicationException actual)
            {
                Require(ReferenceEquals(actual, fatal), "The original fatal exception must be preserved.");
                Require(attempted.Count == 2, "No further views may be attempted after a fatal failure.");
            }
        });
        test("View batch respects a predicate that refuses to skip a failure", () =>
        {
            var refused = new ArgumentException("Unsafe to continue");
            try
            {
                ViewBatch.Create<int, int>(new[] { 1 }, (source, number) => throw refused, error => false);
                throw new Exception("A rejected failure must escape the batch.");
            }
            catch (ArgumentException actual)
            {
                Require(ReferenceEquals(actual, refused), "The batch must defer fatality decisions to its caller.");
            }
        });
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
