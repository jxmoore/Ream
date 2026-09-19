namespace Ream.App.Controls;

/// <summary>Implemented by views that defer expensive work until the row says they are close to the viewport.</summary>
public interface INearAware
{
    void OnNearChanged(bool isNear);
}
