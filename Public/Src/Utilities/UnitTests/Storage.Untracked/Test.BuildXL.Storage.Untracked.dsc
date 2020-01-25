// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Storage.Untracked {

    @@public
    export const dll = BuildXLSdk.test({
        testFramework: importFrom("Sdk.Managed.Testing.XUnit").framework,
        runTestArgs: {
            unsafeTestRunArguments: {
                // These tests require Detours to run itself, so we won't detour the test runner process itself
                runWithUntrackedDependencies: true
            },
        },
        assemblyName: "Test.BuildXL.Storage.Admin",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            ...importFrom("BuildXL.Utilities").Native.securityDlls,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
            Storage.dll,
        ],
    });
}
