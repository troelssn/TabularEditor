﻿using Aga.Controls.Tree;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TabularEditor.TOMWrapper;
using TabularEditor.TreeViewAdvExtension;

namespace TabularEditor
{
    public class TabularUITree : TabularTree, ITreeModel
    {
        public TabularUITree(Model model) : base(model) { }

        public IEnumerable GetChildren(TreePath treePath)
        {
            if (UpdateLocks > 0) throw new InvalidOperationException("Tree enumeration attempted while update in progress");

            List<TabularNamedObject> items = new List<TabularNamedObject>();
            if (treePath.IsEmpty())
            {
                // If no root was specified, use the entire model
                if (Options.HasFlag(LogicalTreeOptions.ShowRoot))
                    items.Add(Model);
                else
                    return GetChildren(Model);
            }
            else
            {
                return GetChildren(treePath.LastNode as ITabularObjectContainer);
            }

            return items;
        }

        TreeDragInformation DragInfo;
        DropMode DragMode;
        private bool SetDropMode(DropMode mode)
        {
            DragMode = mode;
            return mode != DropMode.None;
        }


        #region Handling drag and drop
        /// <summary>
        /// Contains logic for determining if a drag/drop operation is legal. Additionally, specify doDrop = true to actually perform the drop (if it is legal).
        /// </summary>
        /// <param name="sourceNodes"></param>
        /// <param name="targetNode"></param>
        /// <param name="position"></param>
        /// <param name="doDrop"></param>
        /// <returns></returns>
        public bool CanDrop(TreeNodeAdv[] sourceNodes, TreeNodeAdv targetNode, NodePosition position)
        {
            if (sourceNodes == null || sourceNodes.Length == 0) return false;
            DragInfo = TreeDragInformation.FromNodes(sourceNodes, targetNode, position);

            // Must not drop nodes on themselves or any of their children:
            if (sourceNodes.Contains(targetNode)) return false;
            if (sourceNodes.Any(n => targetNode.HasAncestor(n))) return false;

            // Drag operations that require the source and destination table to be the same:
            if (DragInfo.SameTable)
            {
                // Dragging foldered objects into or out of folders:
                if (sourceNodes.All(n => n.Tag is IDetailObject))
                {
                    if (targetNode.Tag is IDetailObjectContainer) return SetDropMode(DropMode.Folder);
                }

                // Dragging into a hierarchy:
                if (DragInfo.TargetHierarchy != null) {

                    // Dragging levels within a hierarchy or between hierarchies:
                    if (sourceNodes.All(n => n.Tag is Level))
                    {
                        return SetDropMode(DragInfo.SameHierarchy ? DropMode.ReorderLevels : DropMode.LevelMove);
                    }

                    // Dragging columns into a hierarchy:
                    if (sourceNodes.All(n => n.Tag is Column))
                    {
                        // Prevent drop if the hierarchy already contains the dragged column(s) as a level:
                        if (DragInfo.TargetHierarchy.Levels.Any(l => sourceNodes.Select(n => n.Tag as Column).Contains(l.Column))) return false;

                        return SetDropMode(DropMode.AddColumns);
                    }
                }
            } else
            {
                // Dragging measures and calculated columns between tables is also allowed:
                if (sourceNodes.All(n => n.Tag is CalculatedColumn || n.Tag is Measure))
                {
                    if (targetNode.Tag is Table && position == NodePosition.Inside) return SetDropMode(DropMode.MoveObject);
                }

            }

            // All other cases not allowed:
            return false;
        }

        public void DoDrop(TreeNodeAdv[] sourceNodes, TreeNodeAdv targetNode, NodePosition position)
        {
            if (!CanDrop(sourceNodes, targetNode, position)) throw new ArgumentException("Invalid drag drop operation.");

            switch(DragMode)
            {
                case DropMode.ReorderLevels: Handler.Actions.ReorderLevels(sourceNodes.Select(n => n.Tag as Level), DragInfo.TargetOrdinal); break;
                case DropMode.LevelMove: Handler.Actions.AddColumnsToHierarchy(sourceNodes.Select(n => (n.Tag as Level).Column), DragInfo.TargetHierarchy, DragInfo.TargetOrdinal); break;
                case DropMode.AddColumns: Handler.Actions.AddColumnsToHierarchy(sourceNodes.Select(n => n.Tag as Column), DragInfo.TargetHierarchy, DragInfo.TargetOrdinal); break;
                case DropMode.Folder: Handler.Actions.SetContainer(sourceNodes.Select(n => n.Tag as IDetailObject), targetNode.Tag as IDetailObjectContainer, Culture); break;
                case DropMode.MoveObject:
                    Handler.Actions.MoveObjects(sourceNodes.Select(n => n.Tag as IDetailObject), targetNode.Tag as Table, Culture);
                    break;
            }
        }

        #endregion

        public bool IsLeaf(TreePath treePath)
        {
            return !(treePath.LastNode is ITabularObjectContainer);
        }

        public event EventHandler<TreeModelEventArgs> NodesChanged;
        public event EventHandler<TreeModelEventArgs> NodesInserted;
        public event EventHandler<TreeModelEventArgs> NodesRemoved;
        public event EventHandler<TreePathEventArgs> StructureChanged;

        public TreePath GetPath(ITabularObject item)
        {
            if (item == null) return TreePath.Empty;

            var stack = new List<object>();

            stack.Add(Model);
            if (item is Table)
            {
                stack.Add(item);
            }
            else if (item is ITabularTableObject)
            {
                stack.Add((item as ITabularTableObject).Table);

                var level = item as Level;
                if (level != null) item = level.Hierarchy;

                if (item is IDetailObject && Options.HasFlag(LogicalTreeOptions.DisplayFolders))
                {
                    var dfo = item as IDetailObject;

                    var pathBits = dfo.Table.Name.ConcatPath(dfo.GetDisplayFolder(Culture)).Split('\\');
                    var folderPath = dfo.Table.Name;
                    for (var i = 1; i < pathBits.Length; i++)
                    {
                        folderPath = folderPath.ConcatPath(pathBits[i]);
                        if (!FolderTree.ContainsKey(folderPath)) Folder.CreateFolder(dfo.Table, FolderHelper.PathFromFullPath(folderPath));
                        stack.Add(FolderTree[folderPath]);
                    }
                }

                stack.Add(item);
                if (level != null) stack.Add(level);
            }
            return new TreePath(stack.ToArray());
        }

        private int onStructureChangedRequests = 0;
        private HashSet<ITabularObject> updateReqs = new HashSet<ITabularObject>();

        public override void OnStructureChanged(TabularNamedObject obj = null)
        {
            if (obj == null)
                OnStructureChanged(new TreePath(Model));
            else
                OnStructureChanged(GetPath(obj));
        }

        public override void OnNodesRemoved(ITabularObject parent, params ITabularObject[] children)
        {
            if (UpdateLocks > 0)
            {
                updateReqs.AddIfNotExists(parent);
                onStructureChangedRequests++;
                return;
            }
            else
            {
                NodesRemoved?.Invoke(this, new TreeModelEventArgs(GetPath(parent), children));
            }
        }

        public override void OnNodesInserted(ITabularObject parent, params ITabularObject[] children)
        {
            if (UpdateLocks > 0)
            {
                updateReqs.AddIfNotExists(parent);
                onStructureChangedRequests++;
                return;
            }
            else
            {
                NodesInserted?.Invoke(this, new TreeModelEventArgs(GetPath(parent), children));
            }
        }

        public override void OnNodesChanged(ITabularObject nodeItem)
        {
            if (UpdateLocks > 0)
            {
                updateReqs.AddIfNotExists(nodeItem);
                //onStructureChangedRequests++;
                return;
            }
            else
            {
                var path = GetPath(nodeItem);
                NodesChanged?.Invoke(this, new TreeModelEventArgs(path, new object[] { }));
            }
        }


        public void OnStructureChanged(TreePath path)
        {
            if (UpdateLocks > 0)
            {
                updateReqs.AddIfNotExists(path.LastNode as ITabularObject);
                onStructureChangedRequests++;
                return;
            }
            else StructureChanged?.Invoke(this, new TreePathEventArgs(path));
        }

        public override void EndUpdate()
        {
            base.EndUpdate();

            if (UpdateLocks == 0 && onStructureChangedRequests > 0)
            {
                if (updateReqs.Count == 1) OnStructureChanged(GetPath(updateReqs.First()));
                else OnStructureChanged();

                onStructureChangedRequests = 0;
                updateReqs.Clear();
            }
        }

    }

    internal enum DropMode
    {
        Folder,
        ReorderLevels,
        LevelMove,
        AddColumns,
        MoveObject,
        None
    }

    internal class TreeDragInformation
    {
        public static TreeDragInformation FromNodes(TreeNodeAdv[] sourceNodes, TreeNodeAdv targetNode, NodePosition position)
        {
            var dragInfo = new TreeDragInformation();

            dragInfo.SourceTable = (sourceNodes.First().Tag as ITabularTableObject)?.Table;
            dragInfo.TargetTable = (targetNode.Tag as ITabularTableObject)?.Table ?? (position == NodePosition.Inside ? targetNode.Tag as Table : null);

            dragInfo.SourceHierarchy = (sourceNodes.First().Tag as Level)?.Hierarchy;
            dragInfo.TargetHierarchy = (targetNode.Tag as Level)?.Hierarchy ?? (position == NodePosition.Inside ? targetNode.Tag as Hierarchy : null);

            dragInfo.TargetFolder = position == NodePosition.Inside ? targetNode.Tag as IDetailObjectContainer : (targetNode.Tag as IDetailObject)?.GetContainer();
            dragInfo.TargetLevel = targetNode.Tag as Level;

            if (dragInfo.TargetLevel != null)
            {
                dragInfo.TargetOrdinal = dragInfo.TargetLevel.Ordinal;
                if (position == NodePosition.After) dragInfo.TargetOrdinal++;
            }
            else if (dragInfo.TargetHierarchy != null)
            {
                dragInfo.TargetOrdinal = dragInfo.TargetHierarchy.Levels.Count;
            }

            return dragInfo;
        }
        
        public int TargetOrdinal = -1;
        public Table SourceTable;
        public Table TargetTable;
        public Hierarchy SourceHierarchy;
        public Hierarchy TargetHierarchy;
        public Level TargetLevel;
        public IDetailObjectContainer TargetFolder;

        public bool SameTable { get
            {
                return TargetTable != null && SourceTable == TargetTable;
            }
        }

        public bool SameHierarchy { get
            {
                return TargetHierarchy != null && SourceHierarchy == TargetHierarchy;
            }
        }
    }

    public static class CollectionHelper
    {
        public static void AddIfNotExists<T>(this ICollection<T> coll, T item)
        {
            if (!coll.Contains(item))
                coll.Add(item);
        }
    }
}
