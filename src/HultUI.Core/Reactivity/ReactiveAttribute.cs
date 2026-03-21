namespace HultUI.Core.Reactivity;

/// <summary>
/// Marks a property on a partial Component class for automatic Signal&lt;T&gt; backing.
/// The source generator replaces the property with a Signal-backed implementation.
/// <example>
/// <code>
/// public partial class Counter : Component
/// {
///     [Reactive] public int Count { get; set; } = 0;
/// }
/// </code>
/// Generates a Signal&lt;int&gt; field and property that reads/writes through it.
/// </example>
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class ReactiveAttribute : Attribute;
