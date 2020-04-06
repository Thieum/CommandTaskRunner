﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TaskRunnerExplorer;
using ProjectTaskRunner.Helpers;
using Task = System.Threading.Tasks.Task;

namespace CommandTaskRunner
{

    [TaskRunnerExport(Constants.FILENAME)]
    class TaskRunnerProvider : ITaskRunner
    {
        private ImageSource _icon;

        [Import]
        internal SVsServiceProvider _serviceProvider = null;

        public TaskRunnerProvider()
        {
            _icon = new BitmapImage(new Uri(@"pack://application:,,,/CommandTaskRunner;component/Resources/project.png"));
        }

        public List<ITaskRunnerOption> Options
        {
            get { return null; }
        }

        public async Task<ITaskRunnerConfig> ParseConfig(ITaskRunnerCommandContext context, string configPath)
        {
            return await Task.Run(() =>
            {
                var userPathConfig = configPath.Replace(Constants.FILENAME, Constants.USERFILENAME);
                ITaskRunnerNode hierarchy = LoadHierarchy(configPath, userPathConfig);

                if (!hierarchy.Children.Any() && !hierarchy.Children.First().Children.Any())
                    return null;

                return new TaskRunnerConfig(hierarchy, _icon);
            });
        }

        private void ApplyVariable(string key, string value, ref string str)
        {
            str = str.Replace(key, value);
        }

        public string SetVariables(string str, string cmdsDir)
        {
            if (str == null)
                return str;

            var dte = (DTE)_serviceProvider.GetService(typeof(DTE));

            Solution sln = dte.Solution;
            IList<Project> projs = GetProjects(dte);
            SolutionBuild build = sln.SolutionBuild;
            var slnCfg = (SolutionConfiguration2)build.ActiveConfiguration;

            Project proj = projs.Cast<Project>().FirstOrDefault(x => x.FileName.Contains(cmdsDir));

            ApplyVariable("$(ConfigurationName)", slnCfg.Name, ref str);
            ApplyVariable("$(DevEnvDir)", Path.GetDirectoryName(dte.FileName), ref str);
            ApplyVariable("$(PlatformName)", slnCfg.PlatformName, ref str);

            ApplyVariable("$(SolutionDir)", Path.GetDirectoryName(sln.FileName), ref str);
            ApplyVariable("$(SolutionExt)", Path.GetExtension(sln.FileName), ref str);
            ApplyVariable("$(SolutionFileName)", Path.GetFileName(sln.FileName), ref str);
            ApplyVariable("$(SolutionName)", Path.GetFileNameWithoutExtension(sln.FileName), ref str);
            ApplyVariable("$(SolutionPath)", sln.FileName, ref str);


            if (proj != null && proj.ConfigurationManager != null) // some types of projects (TwinCat) can have null ConfigurationManager
            {
                Configuration projCfg = proj.ConfigurationManager.ActiveConfiguration;

                if (projCfg.Properties != null) // website folder projects (File -> Add -> Existing Web Site) have null properties
                {
                    string outDir = (string)projCfg.Properties.Item("OutputPath").Value;

                    string projectDir = Path.GetDirectoryName(proj.FileName);
                    string targetFilename = (string)proj.Properties.Item("OutputFileName").Value;
                    string targetPath = Path.Combine(projectDir, outDir, targetFilename);
                    string targetDir = Path.Combine(projectDir, outDir);

                    ApplyVariable("$(OutDir)", outDir, ref str);

                    ApplyVariable("$(ProjectDir)", projectDir, ref str);
                    ApplyVariable("$(ProjectExt)", Path.GetExtension(proj.FileName), ref str);
                    ApplyVariable("$(ProjectFileName)", Path.GetFileName(proj.FileName), ref str);
                    ApplyVariable("$(ProjectName)", proj.Name, ref str);
                    ApplyVariable("$(ProjectPath)", proj.FileName, ref str);

                    ApplyVariable("$(TargetDir)", targetDir, ref str);
                    ApplyVariable("$(TargetExt)", Path.GetExtension(targetFilename), ref str);
                    ApplyVariable("$(TargetFileName)", targetFilename, ref str);
                    ApplyVariable("$(TargetName)", proj.Name, ref str);
                    ApplyVariable("$(TargetPath)", targetPath, ref str);
                }
            }

            if (proj != null)
                MSBuildProject.SetVariables(proj.FileName, ref str);

            return str;
        }

        private ITaskRunnerNode LoadHierarchy(string configPath, string userConfigPath)
        {
            ITaskRunnerNode root = new TaskRunnerNode(Constants.TASK_CATEGORY);

            AppendCommands(ref root, configPath, "Commands", "A list of commands to execute");
            AppendCommands(ref root, userConfigPath, "User Commands", "A list of user commands to execute");
            
            return root;
        }

        private void AppendCommands(ref ITaskRunnerNode root, string configPath, string name, string description)
        {
            string rootDir = Path.GetDirectoryName(configPath);
            IEnumerable<CommandTask> commands = TaskParser.LoadTasks(configPath);

            if (commands == null)
                return;

            var tasks = new TaskRunnerNode(name);
            tasks.Description = description;
            root.Children.Add(tasks);

            foreach (CommandTask command in commands.OrderBy(k => k.Name))
            {
                command.Name = SetVariables(command.Name, rootDir);
                command.WorkingDirectory = SetVariables(command.WorkingDirectory, rootDir);

                string cwd = command.WorkingDirectory ?? rootDir;

                // Add zero width space
                string commandName = command.Name;

                var task = new TaskRunnerNode(commandName, true) {
                    Command = new DynamicTaskRunnerCommand(this, rootDir, cwd, command.FileName, command.Arguments),
                    Description = $"Filename:\t {command.FileName}\r\nArguments:\t {command.Arguments}"
                };

                tasks.Children.Add(task);
            }
        }

        private IList<Project> GetProjects(DTE dte)
        {
            Projects projects = dte.Solution.Projects;
            List<Project> list = new List<Project>();
            var item = projects.GetEnumerator();
            while (item.MoveNext())
            {
                var project = item.Current as Project;
                if (project == null)
                {
                    continue;
                }

                if (project.Kind == EnvDTE.Constants.vsProjectKindSolutionItems)
                {
                    list.AddRange(GetSolutionFolderProjects(project));
                }
                else
                {
                    list.Add(project);
                }
            }

            return list;
        }

        private IEnumerable<Project> GetSolutionFolderProjects(Project solutionFolder)
        {
            List<Project> list = new List<Project>();
            for (var i = 1; i <= solutionFolder.ProjectItems.Count; i++)
            {
                var subProject = solutionFolder.ProjectItems.Item(i).SubProject;
                if (subProject == null)
                {
                    continue;
                }

                if (subProject.Kind == EnvDTE.Constants.vsProjectKindSolutionItems)
                {
                    list.AddRange(GetSolutionFolderProjects(subProject));
                }
                else
                {
                    list.Add(subProject);
                }
            }
            return list;
        }
    }
}
