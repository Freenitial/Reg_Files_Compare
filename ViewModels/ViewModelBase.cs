// Common base class for every view model in the project.

using CommunityToolkit.Mvvm.ComponentModel;

namespace RegCompare.ViewModels;

/// <summary>
/// Common <see cref="ObservableObject"/>-derived base for all view models.
/// Intentionally empty; provides a single place to add any future cross-cutting
/// concern (e.g. logging context).
/// </summary>
public abstract class ViewModelBase : ObservableObject;
