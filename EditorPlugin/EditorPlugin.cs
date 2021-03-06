﻿using System;

using Duality;
using Duality.IO;
using Duality.Editor;
using Duality.Editor.Properties;
using Duality.Editor.Forms;

using AdamsLair.WinForms.ItemModels;
using Duality.Serialization;

using LibGit2Sharp;
using LibGit2Sharp.Core;
using System.Xml.Linq;

using RockyTV.GitPlugin.Editor.Forms;
using RockyTV.GitPlugin.Editor.Properties;

using System.Windows.Forms;
using System.Text;
using System.IO;

using System.Collections.Generic;
using System.Reflection;

namespace RockyTV.GitPlugin.Editor
{
	/// <summary>
	/// Defines a Duality editor plugin.
	/// </summary>
    public class DualityGitEditorPlugin : EditorPlugin
	{
		private bool isLoading = false;
		private bool isFirstTime = true;
		private bool isRepoInit = false;
		public Repository gitRepo = null;

		private CommitDialog commitDialog = null;
		private DialogResult dialogResult = DialogResult.None;

		private PluginUserData userData = new PluginUserData();
		public PluginUserData UserData
		{
			get { return userData; }
			set { userData = value ?? new PluginUserData(); }
		}

		public override string Id
		{
			get { return "RockyTV.GitPlugin"; }
		}

		protected override void InitPlugin(MainForm main)
		{
			base.InitPlugin(main);

			// Fetch Native Binaries from NuGet
			string nativeBinsDir = Path.Combine(Environment.CurrentDirectory, "NativeBinaries");
			if (!Directory.Exists(nativeBinsDir))
			{
				Directory.CreateDirectory(nativeBinsDir);

				string packageStoreDir = DualityEditorApp.PackageManager.LocalPackageStoreDirectory;

				DirectoryInfo packageDirInfo = new DirectoryInfo(packageStoreDir);
				// Browse FileSystem
				foreach (FileSystemInfo fi in packageDirInfo.GetFileSystemInfos("LibGit2Sharp.NativeBinaries*"))
				{
					// Check if the entry is a Directory
					if (fi.GetType() == typeof(DirectoryInfo))
					{
						DirectoryInfo di = fi as DirectoryInfo;
						foreach (DirectoryInfo childDir in di.GetDirectories())
						{
							if (childDir.Name == "libgit2")
							{
								foreach (DirectoryInfo cd in childDir.GetDirectories())
								{
									if (cd.Name == "osx")
									{
										foreach (FileInfo file in cd.GetFiles())
										{
											string destDir = Path.Combine(nativeBinsDir, cd.Name);
											if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
											File.Copy(file.FullName, Path.Combine(destDir, file.Name), true);
										}
									}
									
									if (cd.Name == "windows" || cd.Name == "linux")
									{
										foreach (DirectoryInfo subDir in cd.GetDirectories())
										{
											foreach (FileInfo file in subDir.GetFiles())
											{
												string destDir = Path.Combine(nativeBinsDir, subDir.Name);
												if (cd.Name == "linux") destDir = Path.Combine(nativeBinsDir, "linux", subDir.Name);
												if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
												File.Copy(file.FullName, Path.Combine(destDir, file.Name), true);
											}
										}
									}
								}
							}
						}
					}
				}
			}

			// Try to initialize our git repository
			try
			{
				isFirstTime = !Repository.IsValid(Environment.CurrentDirectory);
				gitRepo = new Repository(Repository.Init(Environment.CurrentDirectory));
				Log.Editor.Write("Initializing git repository on '" + Environment.CurrentDirectory + "'");
				isRepoInit = true;
			}
			catch (Exception e)
			{
				Log.Editor.WriteError("Failed to initialize repository on '" + Environment.CurrentDirectory + "'");
				Log.Exception(e);
			}

			// Check if .gitignore exists. If it doesn't, create a new one.
			if (!File.Exists(Path.Combine(Environment.CurrentDirectory, ".gitignore")))
			{
				try
				{
					File.WriteAllText(Path.Combine(Environment.CurrentDirectory, ".gitignore"), GenerateGitIgnore());
				}
				catch (Exception e)
				{
					Log.Editor.WriteError("Failed to create .gitignore file:");
					Log.Exception(e);
				}
			}

			// Auto retrieve git user info from global .gitconfig file
			// We set userData.AuthorName and .AuthorEmail to the git value files if both fields are null/empty
			if (string.IsNullOrEmpty(userData.AuthorName) && string.IsNullOrEmpty(userData.AuthorEmail))
			{
				if (userData.AutoFetchConfig)
				{
					using (Configuration gitConfig = new Configuration(null, null, null))
					{
						userData.AuthorName = gitConfig.Get<string>("user.name").Value;
						userData.AuthorEmail = gitConfig.Get<string>("user.email").Value;
					}
				}
			}

			// Check if the repository is initialized to set author config for repository
			if (isRepoInit && !userData.AutoFetchConfig)
			{
				using (Configuration config = gitRepo.Config)
				{
					string authorName = "John Doe";
					string authorEmail = "john.doe@example.com";

					// We want to update our author info according to the user data, so when a field changes, we change it on our repo config
					if (!string.IsNullOrEmpty(userData.AuthorName) || !string.IsNullOrEmpty(userData.AuthorEmail))
					{
						authorName = userData.AuthorName;
						authorEmail = userData.AuthorEmail;
					}

					config.Set("user.name", authorName);
					config.Set("user.email", authorEmail);
				}
			}

			// If this is the first run, create an initial commit by staging .gitignore
			if (isFirstTime && File.Exists(Path.Combine(Environment.CurrentDirectory, ".gitignore")))
			{
				gitRepo.Stage(".gitignore");
				Signature author = new Signature(userData.AuthorName, userData.AuthorEmail, DateTime.Now);
				gitRepo.Commit("Initial commit", author);
			}

			MenuModelItem gitItem = main.MainMenu.RequestItem("Git");
			gitItem.SortValue = MenuModelItem.SortValue_Bottom;
			gitItem.AddItem(new MenuModelItem
			{
				Name = GitRes.MenuItemName_GitSettings,
				ActionHandler = menuItemGitSettings_Click
			});
			gitItem.AddItem(new MenuModelItem
			{
				Name = GitRes.MenuItemName_GitCommit,
				ActionHandler = menuItemCommit_Click,
				ShortcutKeys = Keys.Control | Keys.T,
			});
		}

		private void menuItemCommit_Click(object sender, EventArgs e)
		{
			this.RequestCommitDialog();
		}

		private CommitDialog RequestCommitDialog()
		{
			Dictionary<string, FileStatus> fileStatuses = new Dictionary<string, FileStatus>();
			if (commitDialog == null || commitDialog.IsDisposed)
			{
				foreach (var item in gitRepo.RetrieveStatus())
				{
					string fullPath = Path.Combine(Environment.CurrentDirectory, item.FilePath);
					if (!fileStatuses.ContainsKey(fullPath))
						fileStatuses.Add(fullPath, item.State);
				}

				commitDialog = new CommitDialog(fileStatuses);
				commitDialog.FormClosed += delegate (object sender, FormClosedEventArgs e) 
				{
					StageAndCommit(commitDialog.CommitMessage, commitDialog.CommitOptions, commitDialog.StagedFilesList);
					commitDialog.StagedFilesList.Clear();
					commitDialog = null;
				};
			}

			if (!isLoading)
				dialogResult = commitDialog.ShowDialog();

			return commitDialog;
		}

		private void menuItemGitSettings_Click(object sender, EventArgs e)
		{
			DualityEditorApp.Select(this, new ObjectSelection(new[] { this.UserData }));
		}

		protected override void LoadUserData(XElement node)
		{
			isLoading = true;

			bool tryParseBool;
			if (node.GetElementValue("autoFetchConfig", out tryParseBool))
				userData.AutoFetchConfig = tryParseBool;
			if (node.GetElementValue("usefulLogMessages", out tryParseBool))
				userData.UsefulLogMessages = tryParseBool;

			XElement gitElem = node.Element("user");
			if (gitElem != null)
			{
				userData.AuthorName = gitElem.GetElementValue("name");
				userData.AuthorEmail = gitElem.GetElementValue("email");
			}

			isLoading = false;
		}

		protected override void SaveUserData(XElement node)
		{
			node.SetElementValue("autoFetchConfig", userData.AutoFetchConfig);
			node.SetElementValue("usefulLogMessages", userData.UsefulLogMessages);

			XElement gitElem = new XElement("user");
			gitElem.SetElementValue("name", userData.AuthorName);
			gitElem.SetElementValue("email", userData.AuthorEmail);
			node.Add(gitElem);
		}

		private string GenerateGitIgnore()
		{
			StringBuilder sb = new StringBuilder();

			sb.AppendLine(string.Format("# .gitignore generated on {0}", DateTime.Now.ToString()));
			sb.AppendLine("# ENCODING: UTF-8");
			sb.AppendLine();
			sb.AppendLine("# Directories");
			sb.AppendLine("#");
			sb.AppendLine(".git");
			sb.AppendLine("Backup");
			sb.AppendLine("Source/Code/**/bin");
			sb.AppendLine("Source/Code/**/obj");
			sb.AppendLine("Source/Packages");
			sb.AppendLine();
			sb.AppendLine("# Files");
			sb.AppendLine("#");
			sb.AppendLine("*.csproj.user");
			sb.AppendLine("*.suo");
			sb.AppendLine("AppData.dat");
			sb.AppendLine("DualityEditor.exe");
			sb.AppendLine("EditorUserData.xml");
			sb.AppendLine("logfile*.txt");
			//sb.AppendLine("logfile_editor.txt");
			//sb.AppendLine("logfile_editor_prev.txt");
			sb.AppendLine("perflog*.txt");
			//sb.AppendLine("perflog_editor.txt");
			//sb.AppendLine("perflog_editor_prev.txt");
			sb.AppendLine();

			return sb.ToString();
		}

		private void StageAndCommit(string commitMessage, CommitOptions commitOptions, List<string> filesToStage)
		{
			List<string> stagedFiles = new List<string>();

			if (!(string.IsNullOrEmpty(commitMessage) || string.IsNullOrWhiteSpace(commitMessage)))
			{
				if (filesToStage.Count > 0)
				{
					if (gitRepo != null)
					{
						foreach (string file in filesToStage)
						{
							if (!stagedFiles.Contains(file))
							{
								gitRepo.Stage(file);
								stagedFiles.Add(file);
							}
						}

						Signature author = new Signature(userData.AuthorName, userData.AuthorEmail, DateTime.Now);

						try
						{
							gitRepo.Commit(commitMessage, author, commitOptions);
						}
						catch (EmptyCommitException)
						{
							LogMessage("No changes made; nothing to commit.");
						}
						catch (Exception e)
						{
							Log.Exception(e);
						}
					}
				}
			}
		}

		public void LogMessage(string msg, params object[] arg)
		{
			if (userData.UsefulLogMessages)
				Log.Editor.Write(msg, arg);
		}
	}
}
