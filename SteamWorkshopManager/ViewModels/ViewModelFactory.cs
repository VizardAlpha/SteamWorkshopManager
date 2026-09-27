using System;
using Microsoft.Extensions.DependencyInjection;

namespace SteamWorkshopManager.ViewModels;

/// <summary>
/// Builds view-models that take runtime arguments (the item being edited, the
/// upload progress sink...) on top of their DI dependencies.
/// </summary>
public interface IViewModelFactory
{
    T Create<T>(params object[] args) where T : class;
}

public sealed class ViewModelFactory(IServiceProvider services) : IViewModelFactory
{
    public T Create<T>(params object[] args) where T : class =>
        ActivatorUtilities.CreateInstance<T>(services, args);
}
