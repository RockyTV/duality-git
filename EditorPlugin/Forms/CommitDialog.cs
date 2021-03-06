﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;

using LibGit2Sharp;
using LibGit2Sharp.Core;

using System.IO;

using Duality;

using RockyTV.GitPlugin.Editor.Properties;
using RockyTV.GitPlugin.Editor.Extensions;

using EditorPlugin = RockyTV.GitPlugin.Editor.DualityGitEditorPlugin;

namespace RockyTV.GitPlugin.Editor.Forms
{
	public partial class CommitDialog : Form
	{
		private Dictionary<string, FileStatus> FileStatuses;
		public CommitDialog(Dictionary<string, FileStatus>statuses)
		{
			InitializeComponent();
			FileStatuses = statuses;
			Focus();
			PopulateTreeView();
		}

		public List<string> StagedFilesList = new List<string>();
		public string CommitMessage = string.Empty;
		public CommitOptions CommitOptions = new CommitOptions();

		private void ListDirectoryFiles(TreeView treeView, string path)
		{
			ImageList treeImageList = new ImageList();

			treeImageList.Images.Add("Folder", Image.FromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream(GitResNames.ImageFolder)));
			treeImageList.Images.Add("File", Image.FromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream(GitResNames.ImageFile)));
			treeImageList.Images.Add("Dll", Image.FromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream(GitResNames.ImageFileDll)));
			treeImageList.Images.Add("FileAdded", Image.FromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream(GitResNames.ImageFileAdded)));
			treeImageList.Images.Add("DllAdded", Image.FromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream(GitResNames.ImageFileDllAdded)));
			treeImageList.Images.Add("FileRemoved", Image.FromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream(GitResNames.ImageFileRemoved)));
			treeImageList.Images.Add("DllRemoved", Image.FromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream(GitResNames.ImageFileDllRemoved)));
			treeImageList.Images.Add("FileModified", Image.FromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream(GitResNames.ImageFileModified)));
			treeImageList.Images.Add("DllModified", Image.FromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream(GitResNames.ImageFileDllModified)));

			treeImageList.TransparentColor = Color.Transparent;
			treeImageList.ColorDepth = ColorDepth.Depth32Bit;
			treeView.ImageList = treeImageList;

			treeView.Nodes.Clear();

			DirectoryInfo rootDirectoryInfo = new DirectoryInfo(path);

			treeView.Nodes.Add(CreateDirectoryNode(rootDirectoryInfo, true));
		}

		private TreeNode CreateDirectoryNode(DirectoryInfo directoryInfo, bool expanded = false)
		{
			TreeNode directoryNode = new TreeNode(directoryInfo.Name);
			directoryNode.ImageKey = "Folder";
			directoryNode.SelectedImageKey = "Folder";
			directoryNode.Name = directoryInfo.Name;
			directoryNode.Tag = directoryInfo.FullName;
			if (expanded)
				directoryNode.Expand();

			foreach (DirectoryInfo directory in directoryInfo.GetDirectories())
			{
				if (!(directory.Name == ".git" || directory.FullName == Path.Combine(Environment.CurrentDirectory, "Source", "Packages") ||
					directory.FullName == Path.Combine(Environment.CurrentDirectory, "Source", "Code", ".vs") || directory.FullName == Path.Combine(Environment.CurrentDirectory, "Source", "Code", "EditorPlugin", "obj") ||
					directory.FullName == Path.Combine(Environment.CurrentDirectory, "Source", "Code", "EditorPlugin", "bin") || directory.FullName == Path.Combine(Environment.CurrentDirectory, "Source", "Code", "CorePlugin", "obj") ||
					directory.FullName == Path.Combine(Environment.CurrentDirectory, "Source", "Code", "CorePlugin", "bin") || directory.FullName == Path.Combine(Environment.CurrentDirectory, "Backup")))
						directoryNode.Nodes.Add(CreateDirectoryNode(directory));
			}

			foreach (FileInfo file in directoryInfo.GetFiles())
			{
				if (FileStatuses.ContainsKey(file.FullName))
				{
					FileStatus status = FileStatuses[file.FullName];
					if (status != FileStatus.Ignored)
					{
						TreeNode fileNode = new TreeNode(file.Name);
						fileNode.Name = file.Name;

						string imageKey = "File";
						if (Path.GetExtension(file.Name).ToLower().LastIndexOf("dll") != -1) imageKey = "Dll";

						switch (status)
						{
							case FileStatus.Untracked:
							case FileStatus.Added:
								imageKey += "Added";
								break;
							case FileStatus.Removed:
								imageKey += "Removed";
								break;
							case FileStatus.Modified:
								imageKey += "Modified";
								break;
							default:
								break;
						}

						fileNode.ImageKey = imageKey;
						fileNode.SelectedImageKey = imageKey;
						fileNode.Tag = file.FullName;

						directoryNode.Nodes.Add(fileNode);
					}
				}
			}

			return directoryNode;
		}

		private void PopulateTreeView()
		{
			ListDirectoryFiles(fileTreeView, Environment.CurrentDirectory);
		}

		private void buttonCommit_Click(object sender, EventArgs e)
		{
			if (string.IsNullOrEmpty(textBoxMessage.Text) || string.IsNullOrWhiteSpace(textBoxMessage.Text))
				textBoxMessage.Focus();
			else
			{
				CommitOptions commitOptions = new CommitOptions();
				commitOptions.AllowEmptyCommit = false;
				commitOptions.AmendPreviousCommit = false;
				commitOptions.PrettifyMessage = true;

				CommitOptions = commitOptions;
				CommitMessage = textBoxMessage.Text;

				AddCheckedNodesToList();

				DialogResult = DialogResult.OK;
				Close();
			}
		}

		private void buttonCancel_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
			Close();
		}

		private void AddCheckedNodesToList()
		{
			foreach (TreeNode treeNode in fileTreeView.Nodes)
			{
				AddCheckedNodesToList(treeNode);
			}
		}

		private void AddCheckedNodesToList(TreeNode treeNode)
		{
			if (treeNode.Nodes.Count > 0)
				foreach (TreeNode node in treeNode.Nodes)
					AddCheckedNodesToList(node);

			if (treeNode.Checked)
			{
				string fullFilePath = treeNode.Tag.ToString();
				FileAttributes fileAttr = File.GetAttributes(fullFilePath);

				// Do not add directories to the staged files list.
				// Git does not stage directories.
				if (!fileAttr.HasFlag(FileAttributes.Directory))
				{
					if (!StagedFilesList.Contains(fullFilePath))
						StagedFilesList.Add(fullFilePath);
				}
			}
		}

		private void CheckAllChildNodes(TreeNode treeNode, bool nodeChecked)
		{
			foreach (TreeNode node in treeNode.Nodes)
			{
				node.Checked = nodeChecked;
				if (node.Nodes.Count > 0)
					CheckAllChildNodes(node, nodeChecked);
			}
		}

		private void stagedFileList_AfterCheck(object sender, TreeViewEventArgs e)
		{
			if (e.Action != TreeViewAction.Unknown)
			{
				if (e.Node.Nodes.Count > 0)
				{
					CheckAllChildNodes(e.Node, e.Node.Checked);
				}
			}
		}
	}
}
