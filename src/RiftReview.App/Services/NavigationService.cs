using System;

namespace RiftReview.App.Services;

// Lets a non-shell ViewModel ask the AppShell to navigate the NavigationView to a page.
public sealed class NavigationService
{
    public event Action<Type>? NavigationRequested;
    public void NavigateTo(Type pageType) => NavigationRequested?.Invoke(pageType);
}
