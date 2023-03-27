using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MissingRefsFinder
{
    public class Cli
    {
        private static string? GetArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == name && args.Length > i + 1)
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        [MenuItem("Tools/Find All Missing References", false, 50)]
        public static void InspectAll()
        {
            var output_path = GetArg("-outfile");

            var errors = MissingPrefabsFinder.Run();
            errors.Join(MissingReferencesFinder.FindMissingReferencesInAssets());

            if (output_path != null)
            {
                Debug.Log($"Writing summary of errors to {output_path}");
                using var writer = new StreamWriter(output_path, false);
                foreach (var msg in errors.ErrorMessages())
                {
                    writer.WriteLine(msg);
                }
            }
        }
    }
}