// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

const guardianPoliCheckConfigFile = createConfigurationFile(poliCheckConfiguration(), p`${guardianConfigFileDirectory.path}/poliCheckConfiguration.gdnconfig`);
const useUserExclusion = Environment.hasVariable(policheckUserExclusionPath);

export function addPoliCheckCalls(rootDirectory : Directory, guardianToolRoot : StaticDirectory, packageDirectory : StaticDirectory, guardianDrop : Directory, files : File[]) : Transformer.ExecuteResult[] {
    let results : MutableSet<Transformer.ExecuteResult> = MutableSet.empty<Transformer.ExecuteResult>();

    const minFilesPerCall = Environment.hasVariable(filesPerPolicheckCall) ? Environment.getNumberValue(filesPerPolicheckCall) : 1500;
    const additionalCalls = Math.mod(files.length, minFilesPerCall) > 0 ? 1 : 0;
    const numCalls = Math.div(files.length, minFilesPerCall) + additionalCalls;

    for (let i = 0; i < numCalls; i++) {
        const poliCheckWorkDir = Context.getNewOutputDirectory("policheck");

        const scannedFiles = i === numCalls-1 && additionalCalls === 1
            ? files.slice(minFilesPerCall * i, files.length)
            : files.slice(minFilesPerCall * i, minFilesPerCall * (i + 1));

        const scanPaths = scannedFiles.map(file => file.path);
        const txtFile = Transformer.writeAllLines(p`${poliCheckWorkDir.path}/listfiles.txt`, scanPaths);

        const environmentVariables : Transformer.EnvironmentVariable[] = [
            {name: "TF_BUILD", value: true},
            ...addIf(useUserExclusion, {name: "Policheck.UserExclusionPath", value: Environment.getFileValue(policheckUserExclusionPath)}),
        ];
        const dependencies = [
            txtFile, 
            ...scannedFiles, 
            ...addIf(useUserExclusion, Environment.getFileValue(policheckUserExclusionPath)),
        ];
        const sarifName = `policheck_${i.toString()}.sarif`;

        results.add(createGuardianCall(
            guardianToolRoot,
            packageDirectory,
            guardianDrop,
            dependencies,
            `policheck_${i}`,
            poliCheckWorkDir,
            r`${sarifName}`,
            [guardianPoliCheckConfigFile],
            environmentVariables,
            /*retryExitCodes*/undefined,
            /*processRetries*/undefined,
            /*pathDirectories*/undefined,
            /*additionalOutputs*/undefined,
            /*untrackedPaths*/[d`${Context.getMount("ProgramData").path}/Microsoft/Crypto`],
            /*untrackedScopes*/undefined,
            /*allowUndeclaredSourceReads*/false,
            ["COMPUTERNAME"])
        );
    }
    return results.toArray();
}

function poliCheckConfiguration() : Object {
    let arguments = useUserExclusion ? {
        "ListFile": "listfiles.txt",
        "TermTables": 9,
        "ExcludeGlobalTermTable": false,
        "CommentScanning": 0,
        "SubfolderScanning": 1,
        "UserExclusionPath": "$(Policheck.UserExclusionPath)"
    } : {
        "ListFile": "listfiles.txt",
        "TermTables": 9,
        "ExcludeGlobalTermTable": false,
        "CommentScanning": 0,
        "SubfolderScanning": 1
    };

    return {
        "fileVersion": "1.1.2",
        "tools": [
            {
                "fileVersion": "1.1.2",
                "tool": {
                    "name": "policheck",
                    "version": "latest"
                },
                "arguments": arguments,
                "outputExtension": "xml",
                "successfulExitCodes": [
                    0
                ],
                "errorExitCodes": {
                    "1": "Canceled operation error.",
                    "2": "Invalid arguments error.",
                    "3": "Application error.",
                    "4": "Policheck service error.",
                    "5": "KeyVault operation error.",
                    "6": "OnePolicheck error.",
                    "7": "Infrastructure error.",
                    "8": "Unhandled error.",
                    "9": "Policheck data error.",
                    "255": "The tool crashed. Please contact the tool owner for support."
                },
                "outputPaths": [ ]
            }
        ]
    };
}