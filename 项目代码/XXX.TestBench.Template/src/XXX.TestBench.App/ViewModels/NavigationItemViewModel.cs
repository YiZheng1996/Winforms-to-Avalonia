namespace XXX.TestBench.App.ViewModels;

public sealed class NavigationItemViewModel
{
    public NavigationItemViewModel(string title, PageViewModel page)
    {
        Title = title;
        Page = page;
    }

    public string Title { get; }
    public PageViewModel Page { get; }
}
