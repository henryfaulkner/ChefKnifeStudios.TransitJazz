using System;

namespace ChefKnifeStudios.TransitJazz.Client.Shared.Attributes;

/// <summary>
/// Marks a <see cref="Models.Settings"/> property as hidden from the end-user in the
/// SettingsBlade UI. The setting is still persisted and still takes effect — it is only
/// omitted from the rendered checkbox list. Use for dev-only or deprecated settings.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class HiddenSettingAttribute : Attribute
{
}
