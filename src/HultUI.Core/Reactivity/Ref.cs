namespace HultUI.Core.Reactivity;

/// <summary>
/// Type-erased interface for element refs, used internally by PatchFrom.
/// </summary>
internal interface IRef
{
    void SetTarget(object target);
}

/// <summary>
/// A stable reference container for elements. Handlers capture the Ref
/// (which never changes) and read <see cref="Current"/> at invocation time.
///
/// During Build, <c>element.WithRef(myRef)</c> points the ref at the newly
/// created element. When PatchFrom merges that element into the live tree,
/// the ref is updated to point to the live element instead — so handlers
/// always reach the element that's actually in the tree.
/// </summary>
public class Ref<T> : IRef where T : class
{
    public T? Current { get; set; }

    void IRef.SetTarget(object target) => Current = (T)target;
}
