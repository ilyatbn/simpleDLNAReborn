using System.Collections.Generic;

namespace NMaier.SimpleDlna.Admin.Api
{
  /// <summary>
  ///   Describes the parameters each configurable view accepts.
  /// </summary>
  /// <remarks>
  ///   IRepositoryItem exposes only Name and Description, so a view's accepted
  ///   parameters are implicit in its SetParameters body and cannot be
  ///   discovered at runtime. This table is the cheap option; the durable fix
  ///   is an interface on IView, which would touch server/.
  ///
  ///   Keep in step with server/Views/{DimensionView,FilterView,LargeView,
  ///   NewView}.cs.
  /// </remarks>
  internal static class ViewParameters
  {
    private static readonly Dictionary<string, List<ViewParameterDto>> table =
      new Dictionary<string, List<ViewParameterDto>>
      {
        {
          "large", new List<ViewParameterDto>
          {
            new ViewParameterDto("size", "uint", "MB", "300",
              "Minimum file size to include")
          }
        },
        {
          "new", new List<ViewParameterDto>
          {
            new ViewParameterDto("date", "date", null, null,
              "Only show files newer than this date (default: 7 days ago)")
          }
        },
        {
          "dimension", new List<ViewParameterDto>
          {
            new ViewParameterDto("min", "dimension", "WxH", null,
              "Minimum width and height, e.g. 1280x720"),
            new ViewParameterDto("max", "dimension", "WxH", null,
              "Maximum width and height"),
            new ViewParameterDto("minwidth", "uint", "px", null, null),
            new ViewParameterDto("maxwidth", "uint", "px", null, null),
            new ViewParameterDto("minheight", "uint", "px", null, null),
            new ViewParameterDto("maxheight", "uint", "px", null, null)
          }
        },
        {
          "filter", new List<ViewParameterDto>
          {
            new ViewParameterDto("pattern", "glob", null, null,
              "Only show files whose title or path matches, e.g. *.mkv")
          }
        }
      };

    public static List<ViewParameterDto> Describe(string view)
    {
      List<ViewParameterDto> rv;
      return table.TryGetValue(view ?? string.Empty, out rv) ? rv : null;
    }
  }
}
