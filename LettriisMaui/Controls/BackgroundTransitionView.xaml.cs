using System.Windows.Input;

namespace LettriisMaui.Controls;

public partial class BackgroundTransitionView : ContentView
{
    public static readonly BindableProperty SourceProperty =
        BindableProperty.Create(
            nameof(Source),
            typeof(ImageSource),
            typeof(BackgroundTransitionView),
            default(ImageSource),
            propertyChanged: OnSourceChanged);

    public static readonly BindableProperty DurationMsProperty =
        BindableProperty.Create(
            nameof(DurationMs),
            typeof(uint),
            typeof(BackgroundTransitionView),
            450u);

    public static readonly BindableProperty IsEnabledProperty =
        BindableProperty.Create(
            nameof(IsEnabled),
            typeof(bool),
            typeof(BackgroundTransitionView),
            true);

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public uint DurationMs
    {
        get => (uint)GetValue(DurationMsProperty);
        set => SetValue(DurationMsProperty, value);
    }

    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    bool _initialized;

    public BackgroundTransitionView()
    {
        InitializeComponent();
    }

    static async void OnSourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not BackgroundTransitionView view)
            return;

        var newSource = newValue as ImageSource;
        if (newSource is null)
            return;

        // First set: no animation (avoid flash)
        if (!view._initialized)
        {
            view.OldImage.Source = newSource;
            view.OldImage.Opacity = 1;
            view.NewImage.Opacity = 0;
            view._initialized = true;
            return;
        }

        if (!view.IsEnabled)
        {
            view.OldImage.Source = newSource;
            view.OldImage.Opacity = 1;
            view.NewImage.Opacity = 0;
            return;
        }

        // Cross-fade
        view.NewImage.Source = newSource;
        view.NewImage.Opacity = 0;

        var dur = view.DurationMs;

        try
        {
            await Task.WhenAll(
                view.NewImage.FadeTo(1, dur, Easing.CubicOut),
                view.OldImage.FadeTo(0, dur, Easing.CubicIn)
            );

            // Swap
            view.OldImage.Source = newSource;
            view.OldImage.Opacity = 1;
            view.NewImage.Opacity = 0;
        }
        catch
        {
            // If animation cancelled (page disappearing), just snap to final
            view.OldImage.Source = newSource;
            view.OldImage.Opacity = 1;
            view.NewImage.Opacity = 0;
        }
    }
}