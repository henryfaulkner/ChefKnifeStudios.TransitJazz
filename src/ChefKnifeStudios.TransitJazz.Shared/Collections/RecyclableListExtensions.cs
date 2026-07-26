using System.Collections.Generic;

namespace ChefKnifeStudios.TransitJazz.Shared.Collections;

/// <summary>
/// Helpers for the three canonical <see cref="RecyclableList{T}"/> usage patterns: buffering an
/// existing sequence into a pooled list, converting a pooled list to a plain collection, and
/// producing a self-disposing returned sequence.
/// </summary>
public static class RecyclableListExtensions
{
    /// <summary>
    /// Buffers <paramref name="source"/> into a new <see cref="RecyclableList{T}"/>, pre-sizing when
    /// the source count is known.
    /// </summary>
    /// <remarks>
    /// <b>The caller owns disposal</b> of the returned list. Dispose it with a <c>using</c> for
    /// method-scope use, or tie its lifetime to an external owner — e.g. inside an ASP.NET handler,
    /// <c>httpContext.Response.RegisterForDispose(buffer)</c> returns the buffer to the pool when the
    /// request ends. (That registration lives in the WebAPI project; this Shared library takes no
    /// ASP.NET dependency.)
    /// </remarks>
    public static RecyclableList<T> AsRecyclableList<T>(this IEnumerable<T> source) => new(source);

    /// <summary>
    /// Returns a new <see cref="List{T}"/> copy of the live region of <paramref name="list"/>.
    /// </summary>
    /// <remarks>This makes a single copy; the pooled buffer is unaffected and still owned by
    /// <paramref name="list"/>.</remarks>
    public static List<T> AsList<T>(this RecyclableList<T> list) => new(list);

    /// <summary>Returns the same instance typed as <see cref="IList{T}"/> (identity cast).</summary>
    public static IList<T> AsIList<T>(this RecyclableList<T> list) => list;

    /// <summary>
    /// Wraps <paramref name="list"/> in a one-shot sequence that disposes the underlying list — returning
    /// its buffer to the pool — when enumeration completes or the enumerator is disposed (including on an
    /// early <c>foreach</c> break). The caller writes no disposal code.
    /// </summary>
    public static IEnumerable<T> AsEnumerableRegisteredToDispose<T>(this RecyclableList<T> list)
    {
        try
        {
            foreach (T item in list)
                yield return item;
        }
        finally
        {
            list.Dispose();
        }
    }
}
