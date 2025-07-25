using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public class BranchTreeNodeIcon : UserControl
    {
        public static readonly StyledProperty<bool> IsExpandedProperty =
            AvaloniaProperty.Register<BranchTreeNodeIcon, bool>(nameof(IsExpanded));

        public bool IsExpanded
        {
            get => GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            UpdateContent();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == IsExpandedProperty)
                UpdateContent();
        }

        private void UpdateContent()
        {
            if (DataContext is not ViewModels.BranchTreeNode node)
            {
                Content = null;
                return;
            }

            if (node.Backend is Models.Remote)
            {
                CreateContent(new Thickness(0, 0, 0, 0), "Icons.Remote", false);
            }
            else if (node.Backend is Models.Branch branch)
            {
                if (branch.IsCurrent)
                    CreateContent(new Thickness(0, 0, 0, 0), "Icons.CheckCircled", true);
                else
                    CreateContent(new Thickness(2, 0, 0, 0), "Icons.Branch", false);
            }
            else
            {
                if (node.IsExpanded)
                    CreateContent(new Thickness(0, 2, 0, 0), "Icons.Folder.Open", false);
                else
                    CreateContent(new Thickness(0, 2, 0, 0), "Icons.Folder", false);
            }
        }

        private void CreateContent(Thickness margin, string iconKey, bool highlight)
        {
            if (this.FindResource(iconKey) is not StreamGeometry geo)
                return;

            var path = new Path()
            {
                Width = 12,
                Height = 12,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = margin,
                Data = geo,
            };

            if (highlight)
                path.Fill = Brushes.Green;

            Content = path;
        }
    }

    public class BranchTreeNodeToggleButton : ToggleButton
    {
        protected override Type StyleKeyOverride => typeof(ToggleButton);

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
                DataContext is ViewModels.BranchTreeNode { IsBranch: false } node)
            {
                var tree = this.FindAncestorOfType<BranchTree>();
                tree?.ToggleNodeIsExpanded(node);
            }

            e.Handled = true;
        }
    }

    public class BranchTreeNodeTrackStatusPresenter : Control
    {
        public static readonly StyledProperty<FontFamily> FontFamilyProperty =
            TextBlock.FontFamilyProperty.AddOwner<BranchTreeNodeTrackStatusPresenter>();

        public FontFamily FontFamily
        {
            get => GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        public static readonly StyledProperty<double> FontSizeProperty =
           TextBlock.FontSizeProperty.AddOwner<BranchTreeNodeTrackStatusPresenter>();

        public double FontSize
        {
            get => GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        public static readonly StyledProperty<IBrush> ForegroundProperty =
            AvaloniaProperty.Register<BranchTreeNodeTrackStatusPresenter, IBrush>(nameof(Foreground), Brushes.White);

        public IBrush Foreground
        {
            get => GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public static readonly StyledProperty<IBrush> BackgroundProperty =
            AvaloniaProperty.Register<BranchTreeNodeTrackStatusPresenter, IBrush>(nameof(Background), Brushes.White);

        public IBrush Background
        {
            get => GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }

        static BranchTreeNodeTrackStatusPresenter()
        {
            AffectsMeasure<BranchTreeNodeTrackStatusPresenter>(
                FontSizeProperty,
                FontFamilyProperty,
                ForegroundProperty);

            AffectsRender<BranchTreeNodeTrackStatusPresenter>(
                ForegroundProperty,
                BackgroundProperty);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            if (_label != null)
            {
                context.DrawRectangle(Background, null, new RoundedRect(new Rect(8, 0, _label.Width + 18, 18), new CornerRadius(9)));
                context.DrawText(_label, new Point(17, 9 - _label.Height * 0.5));
            }
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            InvalidateMeasure();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            _label = null;

            if (DataContext is ViewModels.BranchTreeNode { Backend: Models.Branch branch })
            {
                var status = branch.TrackStatus.ToString();
                if (!string.IsNullOrEmpty(status))
                {
                    _label = new FormattedText(
                        status,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(FontFamily),
                        FontSize,
                        Foreground);
                }
            }

            return _label != null ? new Size(_label.Width + 18 /* Padding */ + 16 /* Margin */, 18) : new Size(0, 0);
        }

        private FormattedText _label = null;
    }

    public partial class BranchTree : UserControl
    {
        public static readonly StyledProperty<List<ViewModels.BranchTreeNode>> NodesProperty =
            AvaloniaProperty.Register<BranchTree, List<ViewModels.BranchTreeNode>>(nameof(Nodes));

        public List<ViewModels.BranchTreeNode> Nodes
        {
            get => GetValue(NodesProperty);
            set => SetValue(NodesProperty, value);
        }

        public AvaloniaList<ViewModels.BranchTreeNode> Rows
        {
            get;
            private set;
        } = new AvaloniaList<ViewModels.BranchTreeNode>();

        public static readonly RoutedEvent<RoutedEventArgs> SelectionChangedEvent =
            RoutedEvent.Register<BranchTree, RoutedEventArgs>(nameof(SelectionChanged), RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

        public event EventHandler<RoutedEventArgs> SelectionChanged
        {
            add { AddHandler(SelectionChangedEvent, value); }
            remove { RemoveHandler(SelectionChangedEvent, value); }
        }

        public static readonly RoutedEvent<RoutedEventArgs> RowsChangedEvent =
            RoutedEvent.Register<BranchTree, RoutedEventArgs>(nameof(RowsChanged), RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

        public event EventHandler<RoutedEventArgs> RowsChanged
        {
            add { AddHandler(RowsChangedEvent, value); }
            remove { RemoveHandler(RowsChangedEvent, value); }
        }

        public BranchTree()
        {
            InitializeComponent();
        }

        public void Select(Models.Branch branch)
        {
            if (branch == null)
                return;

            var treePath = new List<ViewModels.BranchTreeNode>();
            FindTreePath(treePath, Nodes, branch.Name, 0);

            if (treePath.Count == 0)
                return;

            var oldRowCount = Rows.Count;
            var rows = Rows;
            for (var i = 0; i < treePath.Count - 1; i++)
            {
                var node = treePath[i];
                if (!node.IsExpanded)
                {
                    node.IsExpanded = true;

                    var idx = rows.IndexOf(node);
                    var subtree = new List<ViewModels.BranchTreeNode>();
                    MakeRows(subtree, node.Children, node.Depth + 1);
                    rows.InsertRange(idx + 1, subtree);
                }
            }

            var target = treePath[^1];
            BranchesPresenter.SelectedItem = target;
            BranchesPresenter.ScrollIntoView(target);

            if (oldRowCount != rows.Count)
                RaiseEvent(new RoutedEventArgs(RowsChangedEvent));
        }

        public void UnselectAll()
        {
            BranchesPresenter.SelectedItem = null;
        }

        public void ToggleNodeIsExpanded(ViewModels.BranchTreeNode node)
        {
            _disableSelectionChangingEvent = true;
            node.IsExpanded = !node.IsExpanded;

            var rows = Rows;
            var depth = node.Depth;
            var idx = rows.IndexOf(node);
            if (idx == -1)
                return;

            if (node.IsExpanded)
            {
                var subtree = new List<ViewModels.BranchTreeNode>();
                MakeRows(subtree, node.Children, depth + 1);
                rows.InsertRange(idx + 1, subtree);
            }
            else
            {
                var removeCount = 0;
                for (int i = idx + 1; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row.Depth <= depth)
                        break;

                    row.IsSelected = false;
                    removeCount++;
                }
                rows.RemoveRange(idx + 1, removeCount);
            }

            var repo = DataContext as ViewModels.Repository;
            repo?.UpdateBranchNodeIsExpanded(node);

            RaiseEvent(new RoutedEventArgs(RowsChangedEvent));
            _disableSelectionChangingEvent = false;
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);

            if (Bounds.Height >= 23.0)
                BranchesPresenter.Height = Bounds.Height;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == NodesProperty)
            {
                Rows.Clear();

                if (Nodes is { Count: > 0 })
                {
                    var rows = new List<ViewModels.BranchTreeNode>();
                    MakeRows(rows, Nodes, 0);
                    Rows.AddRange(rows);
                }

                RaiseEvent(new RoutedEventArgs(RowsChangedEvent));
            }
            else if (change.Property == IsVisibleProperty)
            {
                RaiseEvent(new RoutedEventArgs(RowsChangedEvent));
            }
        }

        private void OnNodePointerPressed(object sender, PointerPressedEventArgs e)
        {
            var p = e.GetCurrentPoint(this);
            if (!p.Properties.IsLeftButtonPressed)
                return;

            if (DataContext is not ViewModels.Repository repo)
                return;

            if (sender is not Border { DataContext: ViewModels.BranchTreeNode node })
                return;

            if (node.Backend is not Models.Branch branch)
                return;

            if (BranchesPresenter.SelectedItems is { Count: > 0 })
            {
                var ctrl = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
                if (e.KeyModifiers.HasFlag(ctrl) || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    return;
            }

            repo.NavigateToCommit(branch.Head);
        }

        private void OnNodesSelectionChanged(object _, SelectionChangedEventArgs e)
        {
            if (_disableSelectionChangingEvent)
                return;

            var repo = DataContext as ViewModels.Repository;
            if (repo?.Settings == null)
                return;

            foreach (var item in e.AddedItems)
            {
                if (item is ViewModels.BranchTreeNode node)
                    node.IsSelected = true;
            }

            foreach (var item in e.RemovedItems)
            {
                if (item is ViewModels.BranchTreeNode node)
                    node.IsSelected = false;
            }

            var selected = BranchesPresenter.SelectedItems;
            if (selected == null || selected.Count == 0)
                return;

            ViewModels.BranchTreeNode prev = null;
            foreach (var row in Rows)
            {
                if (row.IsSelected)
                {
                    if (prev is { IsSelected: true })
                    {
                        var prevTop = prev.CornerRadius.TopLeft;
                        prev.CornerRadius = new CornerRadius(prevTop, 0);
                        row.CornerRadius = new CornerRadius(0, 4);
                    }
                    else
                    {
                        row.CornerRadius = new CornerRadius(4);
                    }
                }

                prev = row;
            }

            RaiseEvent(new RoutedEventArgs(SelectionChangedEvent));
        }

        private void OnTreeContextRequested(object _1, ContextRequestedEventArgs _2)
        {
            var repo = DataContext as ViewModels.Repository;
            if (repo?.Settings == null)
                return;

            var selected = BranchesPresenter.SelectedItems;
            if (selected == null || selected.Count == 0)
                return;

            if (selected.Count == 1 && selected[0] is ViewModels.BranchTreeNode { Backend: Models.Remote remote })
            {
                var menu = repo.CreateContextMenuForRemote(remote);
                menu?.Open(this);
                return;
            }

            var branches = new List<Models.Branch>();
            foreach (var item in selected)
            {
                if (item is ViewModels.BranchTreeNode node)
                    CollectBranchesInNode(branches, node);
            }

            if (branches.Count == 1)
            {
                var branch = branches[0];
                var menu = branch.IsLocal ?
                    repo.CreateContextMenuForLocalBranch(branch) :
                    repo.CreateContextMenuForRemoteBranch(branch);
                menu?.Open(this);
            }
            else if (branches.Find(x => x.IsCurrent) == null)
            {
                var menu = new ContextMenu();

                var mergeMulti = new MenuItem();
                mergeMulti.Header = App.Text("BranchCM.MergeMultiBranches", branches.Count);
                mergeMulti.Icon = App.CreateMenuIcon("Icons.Merge");
                mergeMulti.Click += (_, ev) =>
                {
                    repo.MergeMultipleBranches(branches);
                    ev.Handled = true;
                };
                menu.Items.Add(mergeMulti);
                menu.Items.Add(new MenuItem() { Header = "-" });

                var deleteMulti = new MenuItem();
                deleteMulti.Header = App.Text("BranchCM.DeleteMultiBranches", branches.Count);
                deleteMulti.Icon = App.CreateMenuIcon("Icons.Clear");
                deleteMulti.Click += (_, ev) =>
                {
                    repo.DeleteMultipleBranches(branches, branches[0].IsLocal);
                    ev.Handled = true;
                };
                menu.Items.Add(deleteMulti);

                menu.Open(this);
            }
        }

        private void OnTreeKeyDown(object _, KeyEventArgs e)
        {
            if (e.Key is not (Key.Delete or Key.Back))
                return;

            var repo = DataContext as ViewModels.Repository;
            if (repo?.Settings == null)
                return;

            var selected = BranchesPresenter.SelectedItems;
            if (selected == null || selected.Count == 0)
                return;

            if (selected.Count == 1 && selected[0] is ViewModels.BranchTreeNode { Backend: Models.Remote remote })
            {
                repo.DeleteRemote(remote);
                e.Handled = true;
                return;
            }

            var branches = new List<Models.Branch>();
            foreach (var item in selected)
            {
                if (item is ViewModels.BranchTreeNode node)
                    CollectBranchesInNode(branches, node);
            }

            if (branches.Find(x => x.IsCurrent) != null)
                return;

            if (branches.Count == 1)
                repo.DeleteBranch(branches[0]);
            else
                repo.DeleteMultipleBranches(branches, branches[0].IsLocal);

            e.Handled = true;
        }

        private void OnDoubleTappedBranchNode(object sender, TappedEventArgs _)
        {
            if (sender is Grid { DataContext: ViewModels.BranchTreeNode node })
            {
                if (node.Backend is Models.Branch branch)
                {
                    if (branch.IsCurrent)
                        return;

                    if (DataContext is ViewModels.Repository { Settings: not null } repo)
                        repo.CheckoutBranch(branch);
                }
                else
                {
                    ToggleNodeIsExpanded(node);
                }
            }
        }

        private void MakeRows(List<ViewModels.BranchTreeNode> rows, List<ViewModels.BranchTreeNode> nodes, int depth)
        {
            foreach (var node in nodes)
            {
                node.Depth = depth;
                node.IsSelected = false;
                rows.Add(node);

                if (!node.IsExpanded || node.Backend is Models.Branch)
                    continue;

                MakeRows(rows, node.Children, depth + 1);
            }
        }

        private void CollectBranchesInNode(List<Models.Branch> outs, ViewModels.BranchTreeNode node)
        {
            if (node.Backend is Models.Branch branch && !outs.Contains(branch))
            {
                outs.Add(branch);
                return;
            }

            foreach (var sub in node.Children)
                CollectBranchesInNode(outs, sub);
        }

        private void FindTreePath(List<ViewModels.BranchTreeNode> outPath, List<ViewModels.BranchTreeNode> collection, string path, int start)
        {
            if (start >= path.Length - 1)
                return;

            var sepIdx = path.IndexOf('/', start);
            var name = sepIdx < 0 ? path.Substring(start) : path.Substring(start, sepIdx - start);
            foreach (var node in collection)
            {
                if (node.Name.Equals(name, StringComparison.Ordinal))
                {
                    outPath.Add(node);
                    FindTreePath(outPath, node.Children, path, sepIdx + 1);
                }
            }
        }

        private bool _disableSelectionChangingEvent = false;
    }
}
