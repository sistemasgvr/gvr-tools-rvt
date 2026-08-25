using System;
using System.Windows.Media;
using Autodesk.Revit.UI;

namespace GvrTools.Revit.Ribbon
{
    /// <summary>
    /// Convenience base class so a tool only declares what actually differs from the defaults.
    /// A minimal tool is four overrides: <see cref="Id"/>, <see cref="Title"/>,
    /// <see cref="PanelName"/> and <see cref="CommandType"/>.
    /// </summary>
    public abstract class RevitToolBase : IRevitTool
    {
        public abstract string Id { get; }

        public abstract string Title { get; }

        public abstract string PanelName { get; }

        public abstract Type CommandType { get; }

        public virtual int SortOrder => 100;

        public virtual string Tooltip => Title?.Replace(Environment.NewLine, " ").Replace("\n", " ");

        public virtual string LongDescription => null;

        public virtual ImageSource CreateIcon() => null;

        public virtual bool IsSupported(UIControlledApplication application) => true;

        /// <inheritdoc cref="IRevitTool.RequiredFeature"/>
        public virtual string RequiredFeature => null;
    }
}
