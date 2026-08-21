using Aurum.Core;
using Aurum.Infrastructure.Windows;

namespace Aurum.Core.SelfTests;

internal static partial class Program
{
    private static readonly RegistryTarget FirstTarget = new(
        RegistryHiveId.CurrentUser,
        @"Software\AurumTests",
        "First");

    private static readonly RegistryTarget SecondTarget = new(
        RegistryHiveId.CurrentUser,
        @"Software\AurumTests",
        "Second");

    public static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("Apply captures values and revert restores exact state", ApplyAndRevertRestoresExactStateAsync),
            ("Failed multi-value apply rolls back earlier writes", FailedApplyRollsBackAsync),
            ("External changes are reported as drift", ExternalChangeIsReportedAsDriftAsync),
            ("Repair restores desired values without replacing rollback state", RepairPreservesOriginalStateAsync),
            ("Already configured values are not claimed", AlreadyConfiguredIsNotClaimedAsync),
            ("Rollback snapshot is persisted before the first write", TweakSnapshotIsPersistedBeforeFirstWriteAsync),
            ("Revert refuses a snapshot naming an undeclared location", TweakRevertRejectsUndeclaredTargetAsync),
            ("Cleanup deletes only unchanged scanned files", CleanupDeletesOnlyUnchangedFilesAsync),
            ("Hardware monitor returns bounded native metrics", HardwareMonitorReturnsBoundedMetricsAsync),
            ("Power plan apply and revert preserve the original plan", PowerPlanApplyAndRevertAsync),
            ("Power plan drift can be detected and repaired", PowerPlanDriftAndRepairAsync),
            ("Power plan is left untouched when persistence fails", PowerPlanPersistenceFailureLeavesPlanUntouchedAsync),
            ("Power plan records the original before activating another", PowerPlanStateIsPersistedBeforeActivationAsync),
            ("Windows power plan inventory is readable", WindowsPowerPlanInventoryIsReadableAsync),
            ("Storage maintenance requires explicit administrative context", StorageMaintenanceRequiresAdministratorAsync),
            ("ReTrim rejects rotational media", RetrimRejectsRotationalMediaAsync),
            ("Validated ReTrim passes only the selected volume", ValidatedRetrimUsesSelectedVolumeAsync),
            ("Windows storage inventory is readable", WindowsStorageInventoryIsReadableAsync),
            ("Core parking uses an isolated plan and reverts cleanly", CoreParkingApplyAndRevertAsync),
            ("Core parking records its plan before activating it", CoreParkingStateIsPersistedBeforeActivationAsync),
            ("Core parking drift can be repaired", CoreParkingDriftAndRepairAsync),
            ("Core parking refuses conflicting tracked power changes", CoreParkingRejectsPowerConflictAsync),
            ("Power plan refuses conflicting tracked core parking", PowerPlanRejectsCoreParkingConflictAsync),
            ("Service analyzer builds reverse dependencies", ServiceAnalyzerBuildsReverseDependenciesAsync),
            ("Service analyzer classifies safety", ServiceAnalyzerClassifiesSafetyAsync),
            ("Windows service inventory is readable", WindowsServiceInventoryIsReadableAsync),
            ("Network probe validates targets", NetworkProbeValidatesTargetsAsync),
            ("Network probe aggregates latency and loss", NetworkProbeAggregatesLatencyAndLossAsync),
            ("Windows network inventory is readable", WindowsNetworkInventoryIsReadableAsync),
            ("Tweak catalog definitions and profiles are valid", CatalogAndProfilesAreValidAsync),
            ("Service manager disables and reverts services cleanly", ServiceManagerDisableAndRevertAsync),
            ("Service manager detects and repairs drift", ServiceManagerDriftAndRepairAsync),
            ("Service groups are valid and evaluate correctly", ServiceGroupsAndEvaluationAsync),
            ("Service revert restores the delayed auto-start flag", ServiceRevertRestoresDelayedAutoStartAsync),
            ("Service disable refuses protected system services", ServiceDisableRejectsProtectedServiceAsync),
            ("Service repair refuses a protected service named by a tampered snapshot", ServiceRepairRejectsProtectedServiceAsync),
            ("Service revert of an enabled service only drops tracking", ServiceRevertOfEnabledServiceOnlyDropsTrackingAsync),
            ("Storage tuning snapshots and toggles options", StorageTuningSnapshotsAndTogglesAsync),
            ("Storage tuning requires administrative context", StorageTuningRequiresAdminAsync),
            ("Network tuning applies presets and reverts cleanly", NetworkTuningAppliesAndRevertsAsync),
            ("Network tuning requires administrative context", NetworkTuningRequiresAdminAsync),
            ("MSI mode applies gaming preset and reverts cleanly", MsiModeAppliesGamingPresetAndRevertsAsync),
            ("MSI mode requires administrative context", MsiModeRequiresAdminAsync),
            ("MSI revert keeps its snapshot when a device restore fails", MsiRevertKeepsSnapshotOnFailureAsync),
            ("System timer resolution calculations and conversions are accurate", SystemTimerResolutionCalculationsAsync),
            ("System timer resolution edge values and conversions are accurate", SystemTimerResolutionEdgeValuesAsync),
            ("MSI device category icons and labels are assigned correctly", MsiCategoryIconsAndLabelsAreValidAsync),
            ("All UI referenced tweak IDs exist in BuiltInTweakCatalog", AllReferencedTweakIdsExistInCatalogAsync),
            ("Windows PCI device inventory is readable", WindowsPciInventoryIsReadableAsync),
            ("Tweak engine records apply, revert and failed writes in the audit journal", TweakEngineWritesAuditJournalAsync),
            ("JSONL audit journal round-trips newest entries first", JsonlAuditJournalRoundTripsAsync)
        };

        var failures = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"PASS  {test.Name}");
            }
            catch (Exception error)
            {
                failures++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAIL  {test.Name}");
                Console.ResetColor();
                Console.WriteLine(error);
            }
            finally
            {
                Console.ResetColor();
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static async Task ApplyAndRevertRestoresExactStateAsync()
    {
        var store = new InMemorySystemStore();
        store.Seed(FirstTarget, RegistryValue.String("original"));
        var repository = new InMemoryStateRepository();
        var engine = new TweakEngine(store, repository);
        var definition = CreateDefinition();

        await engine.ApplyAsync(definition);

        Equal(RegistryValue.String("changed"), store.Get(FirstTarget), "First value was not applied.");
        Equal(RegistryValue.DWord(1), store.Get(SecondTarget), "Second value was not applied.");
        NotNull(await repository.GetAsync(definition.Id), "Original state was not persisted.");

        await engine.RevertAsync(definition);

        Equal(RegistryValue.String("original"), store.Get(FirstTarget), "Existing value was not restored.");
        True(!store.Contains(SecondTarget), "Originally missing value should have been deleted.");
        True(await repository.GetAsync(definition.Id) is null, "Recovery state was not removed.");
    }

    private static async Task FailedApplyRollsBackAsync()
    {
        var store = new InMemorySystemStore { FailOnWriteNumber = 2 };
        store.Seed(FirstTarget, RegistryValue.String("original"));
        var repository = new InMemoryStateRepository();
        var engine = new TweakEngine(store, repository);
        var definition = CreateDefinition();

        var error = await ThrowsAsync<TweakTransactionException>(() => engine.ApplyAsync(definition));

        True(error.RecoverySucceeded, "Rollback unexpectedly reported recovery errors.");
        Equal(RegistryValue.String("original"), store.Get(FirstTarget), "First write was not rolled back.");
        True(!store.Contains(SecondTarget), "Missing second value was not preserved.");
        True(await repository.GetAsync(definition.Id) is null, "Failed apply left a persisted snapshot.");
    }

    private static async Task ExternalChangeIsReportedAsDriftAsync()
    {
        var store = new InMemorySystemStore();
        var repository = new InMemoryStateRepository();
        var engine = new TweakEngine(store, repository);
        var definition = CreateDefinition();

        await engine.ApplyAsync(definition);
        store.Seed(FirstTarget, RegistryValue.String("external"));

        var evaluation = await engine.EvaluateAsync(definition);
        Equal(TweakStateKind.Drifted, evaluation.State, "External change was not detected.");
    }

    private static async Task AlreadyConfiguredIsNotClaimedAsync()
    {
        var store = new InMemorySystemStore();
        store.Seed(FirstTarget, RegistryValue.String("changed"));
        store.Seed(SecondTarget, RegistryValue.DWord(1));
        var repository = new InMemoryStateRepository();
        var engine = new TweakEngine(store, repository);
        var definition = CreateDefinition();

        var evaluation = await engine.EvaluateAsync(definition);
        Equal(TweakStateKind.AlreadyConfigured, evaluation.State, "Configured state was not detected.");

        await ThrowsAsync<InvalidOperationException>(() => engine.ApplyAsync(definition));
        True(await repository.GetAsync(definition.Id) is null, "Aurum claimed state it did not create.");
    }

    private static async Task RepairPreservesOriginalStateAsync()
    {
        var store = new InMemorySystemStore();
        store.Seed(FirstTarget, RegistryValue.String("original"));
        var repository = new InMemoryStateRepository();
        var engine = new TweakEngine(store, repository);
        var definition = CreateDefinition();

        await engine.ApplyAsync(definition);
        store.Seed(FirstTarget, RegistryValue.String("external"));
        store.Seed(SecondTarget, RegistryValue.DWord(9));

        await engine.RepairAsync(definition);

        Equal(RegistryValue.String("changed"), store.Get(FirstTarget), "Repair did not restore the desired value.");
        Equal(RegistryValue.DWord(1), store.Get(SecondTarget), "Repair did not restore all mutations.");

        await engine.RevertAsync(definition);
        Equal(RegistryValue.String("original"), store.Get(FirstTarget), "Repair replaced the original rollback point.");
        True(!store.Contains(SecondTarget), "Repair replaced the missing-value rollback point.");
    }

    private static async Task TweakSnapshotIsPersistedBeforeFirstWriteAsync()
    {
        var journal = new List<string>();
        var store = new InMemorySystemStore { Journal = journal };
        store.Seed(FirstTarget, RegistryValue.String("original"));
        var repository = new InMemoryStateRepository { Journal = journal };
        var engine = new TweakEngine(store, repository);

        await engine.ApplyAsync(CreateDefinition());

        var saveIndex = journal.IndexOf("snapshot-save");
        var writeIndex = journal.IndexOf("registry-write");
        True(saveIndex >= 0, "Apply never persisted a rollback snapshot.");
        True(
            saveIndex < writeIndex,
            "The rollback snapshot must reach the repository before the first registry write, otherwise a process kill mid-apply strands the change.");
    }

    private static async Task TweakRevertRejectsUndeclaredTargetAsync()
    {
        var store = new InMemorySystemStore();
        store.Seed(FirstTarget, RegistryValue.String("original"));
        var repository = new InMemoryStateRepository();
        var engine = new TweakEngine(store, repository);
        var definition = CreateDefinition();

        await engine.ApplyAsync(definition);

        // Stands in for a snapshot file edited by a process running as the user without
        // elevation. Revert normally runs elevated, so the location it writes must come
        // from the catalog rather than from the file.
        var foreignTarget = new RegistryTarget(
            RegistryHiveId.LocalMachine,
            @"SYSTEM\CurrentControlSet\Services\NotAnAurumTweak",
            "ImagePath");
        var snapshot = await repository.GetAsync(definition.Id)
            ?? throw new Exception("Apply did not persist a snapshot to tamper with.");
        var tamperedEntries = snapshot.Entries.ToList();
        tamperedEntries.Add(new RegistryStateEntry(
            foreignTarget,
            new RegistrySnapshot(true, RegistryValue.String("payload"))));
        await repository.SaveAsync(snapshot with { Entries = tamperedEntries });

        var error = await ThrowsAsync<InvalidOperationException>(() => engine.RevertAsync(definition));

        True(
            error.Message.Contains(foreignTarget.DisplayPath, StringComparison.Ordinal),
            $"Revert failed for an unrelated reason instead of rejecting the undeclared location: {error.Message}");
        True(!store.Contains(foreignTarget), "Revert wrote to a location the tweak never declared.");
        Equal(
            RegistryValue.String("changed"),
            store.Get(FirstTarget),
            "A rejected revert must not restore anything at all.");
        NotNull(await repository.GetAsync(definition.Id), "A rejected revert discarded the rollback snapshot.");
    }

    private static TweakDefinition CreateDefinition() => new(
        "tests.transaction",
        "Tests",
        "Transaction test",
        "Exercises the transaction engine.",
        "No system impact.",
        TweakRisk.Safe,
        RestartRequirement.None,
        false,
        new RegistryMutation(FirstTarget, RegistryValue.String("changed")),
        new RegistryMutation(SecondTarget, RegistryValue.DWord(1)));

    private static async Task CleanupDeletesOnlyUnchangedFilesAsync()
    {
        var root = Path.Combine(Environment.CurrentDirectory, "artifacts", "cleanup-self-test");
        Directory.CreateDirectory(root);
        var deletablePath = Path.Combine(root, "old.tmp");
        var changedPath = Path.Combine(root, "changed.tmp");
        await File.WriteAllTextAsync(deletablePath, "delete me");
        await File.WriteAllTextAsync(changedPath, "original");
        var oldTimestamp = DateTime.UtcNow.AddDays(-3);
        File.SetLastWriteTimeUtc(deletablePath, oldTimestamp);
        File.SetLastWriteTimeUtc(changedPath, oldTimestamp);

        var category = new CleanupCategory(
            "test",
            "Test files",
            "Self-test only.",
            root,
            TimeSpan.FromDays(1));
        var service = new SystemCleanupService([category]);
        var scan = await service.ScanAsync([category.Id]);
        Equal(2, scan.Candidates.Count, "Cleanup scan did not find the expected files.");

        await File.AppendAllTextAsync(changedPath, " changed after scan");
        var result = await service.CleanAsync(scan.Candidates);

        Equal(1, result.DeletedCount, "Cleanup deleted an unexpected number of files.");
        True(!File.Exists(deletablePath), "Unchanged scanned file was not deleted.");
        True(File.Exists(changedPath), "File changed after the scan must be preserved.");

        File.Delete(changedPath);
        Directory.Delete(root);
    }

    private static async Task HardwareMonitorReturnsBoundedMetricsAsync()
    {
        using var monitor = new HardwareMonitorService();
        var inventory = await monitor.CaptureInventoryAsync();
        True(!string.IsNullOrWhiteSpace(inventory.CpuName), "CPU inventory is empty.");
        True(inventory.TotalMemoryBytes > 0, "Installed memory was not detected.");
        True(!string.IsNullOrWhiteSpace(inventory.SystemDriveName), "System drive inventory is empty.");

        _ = await monitor.SampleAsync();
        await Task.Delay(120);
        var sample = await monitor.SampleAsync();
        InRange(sample.CpuUsagePercent, 0, 100, "CPU usage is outside its valid range.");
        InRange(sample.MemoryUsagePercent, 0, 100, "Memory usage is outside its valid range.");
        InRange(sample.DiskUsedPercent, 0, 100, "Disk usage is outside its valid range.");
        if (sample.GpuUsagePercent is not null)
        {
            InRange(sample.GpuUsagePercent.Value, 0, 100, "GPU usage is outside its valid range.");
        }

        True(sample.Uptime >= TimeSpan.Zero, "System uptime cannot be negative.");
    }

    private static async Task PowerPlanApplyAndRevertAsync()
    {
        var originalId = Guid.NewGuid();
        var desiredId = Guid.NewGuid();
        var store = new InMemoryPowerPlanStore(originalId, desiredId);
        var repository = new InMemoryPowerPlanStateRepository();
        var manager = new PowerPlanManager(store, repository);

        await manager.ApplyAsync(desiredId);
        Equal(desiredId, store.ActivePlanId, "Selected power plan was not activated.");
        Equal(originalId, repository.State?.OriginalPlanId, "Original power plan was not persisted.");

        await manager.RevertAsync();
        Equal(originalId, store.ActivePlanId, "Original power plan was not restored.");
        True(repository.State is null, "Power-plan tracking state was not removed after revert.");
    }

    private static async Task PowerPlanDriftAndRepairAsync()
    {
        var originalId = Guid.NewGuid();
        var desiredId = Guid.NewGuid();
        var store = new InMemoryPowerPlanStore(originalId, desiredId);
        var repository = new InMemoryPowerPlanStateRepository();
        var manager = new PowerPlanManager(store, repository);

        await manager.ApplyAsync(desiredId);
        store.ActivePlanId = originalId;
        var evaluation = await manager.EvaluateAsync();
        Equal(PowerPlanStateKind.Drifted, evaluation.State, "External power-plan change was not detected.");

        await manager.RepairAsync();
        Equal(desiredId, store.ActivePlanId, "Tracked power plan was not repaired.");
        Equal(originalId, repository.State?.OriginalPlanId, "Repair replaced the original rollback plan.");
    }

    private static async Task PowerPlanPersistenceFailureLeavesPlanUntouchedAsync()
    {
        var originalId = Guid.NewGuid();
        var desiredId = Guid.NewGuid();
        var store = new InMemoryPowerPlanStore(originalId, desiredId);
        var repository = new InMemoryPowerPlanStateRepository { FailSave = true };
        var manager = new PowerPlanManager(store, repository);

        var error = await ThrowsAsync<PowerPlanTransactionException>(() => manager.ApplyAsync(desiredId));
        True(error.RecoverySucceeded, "Power-plan apply did not report successful recovery.");
        Equal(originalId, store.ActivePlanId, "A failed snapshot save must leave the active plan untouched.");
        True(repository.State is null, "Failed apply left power-plan state behind.");
    }

    private static async Task PowerPlanStateIsPersistedBeforeActivationAsync()
    {
        var originalId = Guid.NewGuid();
        var desiredId = Guid.NewGuid();
        var journal = new List<string>();
        var store = new InMemoryPowerPlanStore(originalId, desiredId) { Journal = journal };
        var repository = new InMemoryPowerPlanStateRepository { Journal = journal };
        var manager = new PowerPlanManager(store, repository);

        await manager.ApplyAsync(desiredId);

        var saveIndex = journal.IndexOf("state-save");
        var activateIndex = journal.IndexOf("plan-activate");
        True(saveIndex >= 0, "Apply never persisted the rollback state.");
        True(
            saveIndex < activateIndex,
            "The original plan must be recorded before the new one is activated, otherwise an interrupted apply leaves no way back.");
    }

    private static async Task WindowsPowerPlanInventoryIsReadableAsync()
    {
        var snapshot = await new WindowsPowerPlanStore().CaptureAsync();
        True(snapshot.Plans.Count > 0, "Windows returned no power plans.");
        True(snapshot.Plans.Any(plan => plan.Id == snapshot.ActivePlanId), "Active Windows power plan is absent from inventory.");
    }

    private static async Task StorageMaintenanceRequiresAdministratorAsync()
    {
        var volume = CreateStorageVolume(StorageMediaKind.SolidState, trimSupported: true);
        var optimizer = new RecordingStorageOptimizer();
        var manager = new StorageMaintenanceManager(
            new InMemoryStorageInventoryStore(volume),
            optimizer,
            () => false);

        var availability = manager.EvaluateAvailability(volume, StorageOperationKind.Retrim);
        True(!availability.CanRun, "ReTrim was offered without administrative access.");
        await ThrowsAsync<InvalidOperationException>(() => manager.RetrimAsync(volume.RootPath));
        True(optimizer.LastOperation is null, "Optimizer ran without administrative access.");
    }

    private static async Task RetrimRejectsRotationalMediaAsync()
    {
        var volume = CreateStorageVolume(StorageMediaKind.HardDisk, trimSupported: true);
        var optimizer = new RecordingStorageOptimizer();
        var manager = new StorageMaintenanceManager(
            new InMemoryStorageInventoryStore(volume),
            optimizer,
            () => true);

        var availability = manager.EvaluateAvailability(volume, StorageOperationKind.Retrim);
        True(!availability.CanRun, "ReTrim was offered for rotational media.");
        await ThrowsAsync<InvalidOperationException>(() => manager.RetrimAsync(volume.RootPath));
        True(optimizer.LastOperation is null, "Optimizer ran ReTrim for rotational media.");
    }

    private static async Task ValidatedRetrimUsesSelectedVolumeAsync()
    {
        var volume = CreateStorageVolume(StorageMediaKind.SolidState, trimSupported: true);
        var optimizer = new RecordingStorageOptimizer();
        var manager = new StorageMaintenanceManager(
            new InMemoryStorageInventoryStore(volume),
            optimizer,
            () => true);

        var result = await manager.RetrimAsync(volume.RootPath);
        Equal(StorageOperationKind.Retrim, optimizer.LastOperation, "Optimizer received the wrong operation.");
        Equal(volume.RootPath, optimizer.LastRootPath, "Optimizer received the wrong volume.");
        True(result.Succeeded, "Fake validated ReTrim did not succeed.");
    }

    private static async Task WindowsStorageInventoryIsReadableAsync()
    {
        var volumes = await new WindowsStorageInventoryStore().CaptureAsync();
        True(volumes.Count > 0, "Windows returned no fixed or removable volumes.");
        True(volumes.Any(static volume => volume.IsSystem), "System volume is absent from storage inventory.");
        True(volumes.All(static volume => volume.TotalBytes > 0), "Storage inventory contains an invalid capacity.");
    }

    private static StorageVolumeInfo CreateStorageVolume(StorageMediaKind mediaKind, bool? trimSupported) => new(
        "C:\\",
        "Tests",
        "NTFS",
        "Test device",
        mediaKind == StorageMediaKind.SolidState ? "NVMe" : "SATA",
        mediaKind,
        1_000_000,
        500_000,
        0,
        trimSupported,
        true,
        true);

    private static async Task CoreParkingApplyAndRevertAsync()
    {
        var store = new InMemoryCoreParkingStore();
        var repository = new InMemoryCoreParkingStateRepository();
        var manager = new CoreParkingManager(store, repository, new InMemoryPowerPlanStateRepository());
        var desired = new CoreParkingSettings(100, 100, 50, 100);
        var originalId = store.ActivePlanId;

        await manager.ApplyAsync(desired);
        NotNull(repository.State, "Core-parking state was not persisted.");
        True(store.ActivePlanId != originalId, "Core parking modified the active plan without cloning it.");
        Equal(desired, await store.ReadSettingsAsync(store.ActivePlanId), "Desired settings were not written to the clone.");

        var managedId = store.ActivePlanId;
        await manager.RevertAsync();
        Equal(originalId, store.ActivePlanId, "Original plan was not restored.");
        True(!await store.ExistsAsync(managedId), "Managed core-parking plan was not deleted.");
        True(repository.State is null, "Core-parking state remained after revert.");
    }

    private static async Task CoreParkingStateIsPersistedBeforeActivationAsync()
    {
        var journal = new List<string>();
        var store = new InMemoryCoreParkingStore { Journal = journal };
        var repository = new InMemoryCoreParkingStateRepository { Journal = journal };
        var manager = new CoreParkingManager(store, repository, new InMemoryPowerPlanStateRepository());

        await manager.ApplyAsync(new CoreParkingSettings(100, 100, 50, 100));

        var saveIndex = journal.IndexOf("state-save");
        var activateIndex = journal.IndexOf("plan-activate");
        var writeIndex = journal.IndexOf("plan-write-settings");
        True(saveIndex >= 0, "Apply never persisted the rollback state.");
        True(
            saveIndex < writeIndex && saveIndex < activateIndex,
            "The managed plan must be recorded before it is populated and activated, otherwise an interrupted apply leaves it active and untracked.");
    }

    private static async Task CoreParkingDriftAndRepairAsync()
    {
        var store = new InMemoryCoreParkingStore();
        var repository = new InMemoryCoreParkingStateRepository();
        var manager = new CoreParkingManager(store, repository, new InMemoryPowerPlanStateRepository());
        var desired = new CoreParkingSettings(75, 100, 25, 100);
        var originalId = store.ActivePlanId;
        await manager.ApplyAsync(desired);
        var managedId = store.ActivePlanId;

        await store.SetActiveAsync(originalId);
        Equal(CoreParkingStateKind.Drifted, (await manager.EvaluateAsync()).State, "Core-parking drift was not detected.");
        await manager.RepairAsync();
        Equal(managedId, store.ActivePlanId, "Core-parking repair did not reactivate the managed plan.");
        Equal(desired, await store.ReadSettingsAsync(managedId), "Core-parking repair did not preserve desired settings.");
    }

    private static async Task CoreParkingRejectsPowerConflictAsync()
    {
        var store = new InMemoryCoreParkingStore();
        var powerRepository = new InMemoryPowerPlanStateRepository
        {
            State = new PersistedPowerPlanState(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow),
        };
        var manager = new CoreParkingManager(store, new InMemoryCoreParkingStateRepository(), powerRepository);
        await ThrowsAsync<InvalidOperationException>(() => manager.ApplyAsync(new CoreParkingSettings(100, 100, 100, 100)));
        Equal(1, store.PlanCount, "A conflicting Core Parking apply created a plan.");
    }

    /// <summary>
    /// The mirror image of <see cref="CoreParkingRejectsPowerConflictAsync"/>. Both
    /// directions must refuse, otherwise two trackers can each claim to own the
    /// rollback for the active power plan.
    /// </summary>
    private static async Task PowerPlanRejectsCoreParkingConflictAsync()
    {
        var originalId = Guid.NewGuid();
        var desiredId = Guid.NewGuid();
        var store = new InMemoryPowerPlanStore(originalId, desiredId);
        var powerRepository = new InMemoryPowerPlanStateRepository();
        var coreParkingRepository = new InMemoryCoreParkingStateRepository();
        await coreParkingRepository.SaveAsync(new PersistedCoreParkingState(
            originalId,
            "Original",
            Guid.NewGuid(),
            new CoreParkingSettings(100, 100, 100, 100),
            DateTimeOffset.UtcNow));

        var manager = new PowerPlanManager(
            store,
            powerRepository,
            async cancellationToken => await coreParkingRepository.GetAsync(cancellationToken) is not null);

        await ThrowsAsync<InvalidOperationException>(() => manager.ApplyAsync(desiredId));
        Equal(originalId, store.ActivePlanId, "A conflicting power-plan apply changed the active plan.");
        True(powerRepository.State is null, "A conflicting power-plan apply persisted tracking state.");
    }

    private static Task ServiceAnalyzerBuildsReverseDependenciesAsync()
    {
        var dependency = CreateService("Dependency", []);
        var consumer = CreateService("Consumer", ["Dependency"]);
        var result = ServiceAnalyzer.Analyze([dependency, consumer]);
        var analyzed = result.Single(item => item.Service.Name == "Dependency");
        True(analyzed.Dependants.Contains("Consumer"), "Reverse service dependency was not built.");
        return Task.CompletedTask;
    }

    private static Task ServiceAnalyzerClassifiesSafetyAsync()
    {
        var result = ServiceAnalyzer.Analyze([
            CreateService("RpcSs", []), CreateService("Spooler", ["RpcSs"]), CreateService("VendorSvc", [])]);
        Equal(ServiceSafetyClass.Protected, result.Single(item => item.Service.Name == "RpcSs").Safety, "RPC was not protected.");
        Equal(ServiceSafetyClass.ContextDependent, result.Single(item => item.Service.Name == "Spooler").Safety, "Spooler was not contextual.");
        Equal(ServiceSafetyClass.Unclassified, result.Single(item => item.Service.Name == "VendorSvc").Safety, "Unknown service was over-classified.");
        return Task.CompletedTask;
    }

    private static async Task WindowsServiceInventoryIsReadableAsync()
    {
        var services = await new WindowsServiceInventory().CaptureAsync();
        True(services.Count > 20, "Windows returned too few services.");
        True(services.Any(item => item.Service.Name.Equals("RpcSs", StringComparison.OrdinalIgnoreCase)), "RPC service is absent from inventory.");
        True(services.All(item => !string.IsNullOrWhiteSpace(item.Service.Name)), "Service inventory contains an empty name.");
    }

    private static async Task NetworkProbeValidatesTargetsAsync()
    {
        Equal("1.1.1.1", NetworkDiagnosticsManager.ValidateTarget(" 1.1.1.1 "), "IPv4 target was not normalized.");
        Equal("example.com", NetworkDiagnosticsManager.ValidateTarget("example.com"), "DNS target was rejected.");
        await ThrowsAsync<ArgumentException>(() => Task.Run(() => NetworkDiagnosticsManager.ValidateTarget("bad target")));
        await ThrowsAsync<ArgumentException>(() => Task.Run(() => NetworkDiagnosticsManager.ValidateTarget("-invalid.example")));
    }

    private static async Task NetworkProbeAggregatesLatencyAndLossAsync()
    {
        var probe = new RecordingNetworkProbe([
            new NetworkProbeSample(true, 10, "ok"),
            new NetworkProbeSample(false, null, "timeout"),
            new NetworkProbeSample(true, 30, "ok"),
            new NetworkProbeSample(true, 20, "ok"),
        ]);
        var manager = new NetworkDiagnosticsManager(new EmptyNetworkInventory(), probe);
        var result = await manager.ProbeAsync("localhost", 4);

        Equal(3, result.Received, "Successful replies were counted incorrectly.");
        Equal(25d, result.LossPercent, "Packet loss was aggregated incorrectly.");
        Equal(20d, result.AverageMilliseconds, "Average latency was aggregated incorrectly.");
        Equal(4, probe.SendCount, "Probe did not send the requested number of packets.");
    }

    private static async Task WindowsNetworkInventoryIsReadableAsync()
    {
        var snapshot = await new WindowsNetworkInventoryStore().CaptureAsync();
        True(snapshot.Adapters.Count > 0, "Windows returned no network adapters.");
        True(snapshot.Adapters.All(static adapter => !string.IsNullOrWhiteSpace(adapter.Name)), "Network inventory contains an empty adapter name.");
        True(snapshot.TcpSettings.Count > 0, "Windows returned no global TCP settings.");
    }

    private static ServiceDefinition CreateService(string name, IReadOnlyList<string> dependencies) => new(
        name, name, string.Empty, ServiceRunState.Running, ServiceStartMode.Automatic, false, 1, dependencies);

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}.");
        }
    }

    private static void NotNull(object? value, string message) => True(value is not null, message);

    private static void InRange(double value, double minimum, double maximum, string message) =>
        True(value >= minimum && value <= maximum, $"{message} Actual: {value}.");

    private static async Task<TException> ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException error)
        {
            return error;
        }

        throw new InvalidOperationException($"Expected exception {typeof(TException).Name} was not thrown.");
    }

    private static Task CatalogAndProfilesAreValidAsync()
    {
        var tweaks = BuiltInTweakCatalog.All;
        True(tweaks.Count >= 10, "Expected at least 10 built-in tweaks.");

        var tweakIds = new HashSet<string>(tweaks.Select(static t => t.Id), StringComparer.Ordinal);
        Equal(tweaks.Count, tweakIds.Count, "Duplicate tweak IDs found in catalog.");

        foreach (var tweak in tweaks)
        {
            True(!string.IsNullOrWhiteSpace(tweak.Name), $"Tweak '{tweak.Id}' has an empty name.");
            True(!string.IsNullOrWhiteSpace(tweak.Category), $"Tweak '{tweak.Id}' has an empty category.");
            True(!string.IsNullOrWhiteSpace(tweak.Description), $"Tweak '{tweak.Id}' has an empty description.");
            True(!string.IsNullOrWhiteSpace(tweak.Impact), $"Tweak '{tweak.Id}' has an empty impact.");
            True(tweak.Mutations.Count > 0, $"Tweak '{tweak.Id}' has no mutations.");
        }

        var profiles = BuiltInTweakCatalog.Profiles;
        True(profiles.Count >= 4, "Expected at least 4 profiles (Balanced, Gaming, Privacy, Laptop).");

        foreach (var profile in profiles)
        {
            True(!string.IsNullOrWhiteSpace(profile.Name), $"Profile '{profile.Id}' has an empty name.");
            True(profile.TweakIds.Count > 0, $"Profile '{profile.Id}' has no tweaks assigned.");

            foreach (var id in profile.TweakIds)
            {
                True(tweakIds.Contains(id), $"Profile '{profile.Id}' references unknown tweak '{id}'.");
            }
        }

        return Task.CompletedTask;
    }

    private static async Task ServiceManagerDisableAndRevertAsync()
    {
        var controlStore = new InMemoryServiceControlStore();
        var stateRepo = new InMemoryServiceStateRepository();
        var manager = new ServiceManager(controlStore, stateRepo);

        controlStore.AddService(new ServiceDefinition(
            "Spooler", "Print Spooler", "Manages print queues",
            ServiceRunState.Running, ServiceStartMode.Automatic, false, 1234, []));

        var initialEval = await manager.EvaluateServiceAsync(controlStore.GetService("Spooler")!);
        Equal(ServiceTrackingState.NotTracked, initialEval.TrackingState, "Expected initial state to be NotTracked.");

        await manager.DisableServiceAsync("Spooler");

        var disabledService = controlStore.GetService("Spooler")!;
        Equal(ServiceStartMode.Disabled, disabledService.StartMode, "Expected service start mode to be Disabled.");
        Equal(ServiceRunState.Stopped, disabledService.State, "Expected service to be stopped.");

        var persisted = await stateRepo.GetAsync("Spooler");
        NotNull(persisted, "Persisted service state was not saved.");
        Equal(ServiceStartMode.Automatic, persisted!.OriginalStartMode, "Original start mode must be preserved.");

        var disabledEval = await manager.EvaluateServiceAsync(disabledService);
        Equal(ServiceTrackingState.Applied, disabledEval.TrackingState, "Expected state to be Applied.");

        await manager.RevertServiceAsync("Spooler");

        var revertedService = controlStore.GetService("Spooler")!;
        Equal(ServiceStartMode.Automatic, revertedService.StartMode, "Expected service start mode to revert to Automatic.");
        Equal(ServiceRunState.Running, revertedService.State, "Expected service to be restarted.");

        var revertedState = await stateRepo.GetAsync("Spooler");
        True(revertedState is null, "State repository should be cleared after revert.");
    }

    private static async Task ServiceManagerDriftAndRepairAsync()
    {
        var controlStore = new InMemoryServiceControlStore();
        var stateRepo = new InMemoryServiceStateRepository();
        var manager = new ServiceManager(controlStore, stateRepo);

        controlStore.AddService(new ServiceDefinition(
            "DiagTrack", "Connected User Experiences and Telemetry", "Telemetry service",
            ServiceRunState.Running, ServiceStartMode.Automatic, false, 5678, []));

        await manager.DisableServiceAsync("DiagTrack");

        // External drift: service re-enabled outside Aurum
        controlStore.ChangeStartMode("DiagTrack", ServiceStartMode.Automatic);
        controlStore.SetState("DiagTrack", ServiceRunState.Running);

        var driftedEval = await manager.EvaluateServiceAsync(controlStore.GetService("DiagTrack")!);
        Equal(ServiceTrackingState.Drifted, driftedEval.TrackingState, "Expected state to be Drifted.");

        await manager.RepairServiceAsync("DiagTrack");

        var repairedService = controlStore.GetService("DiagTrack")!;
        Equal(ServiceStartMode.Disabled, repairedService.StartMode, "Expected repair to set start mode to Disabled.");
        Equal(ServiceRunState.Stopped, repairedService.State, "Expected repair to stop running service.");

        var repairedEval = await manager.EvaluateServiceAsync(repairedService);
        Equal(ServiceTrackingState.Applied, repairedEval.TrackingState, "Expected state to return to Applied.");
    }

    /// <summary>
    /// Windows keeps the delayed-auto-start flag in a configuration value separate from
    /// the start type. Revert must state the captured flag explicitly rather than relying
    /// on that separate value being left untouched.
    /// </summary>
    private static async Task ServiceRevertRestoresDelayedAutoStartAsync()
    {
        var controlStore = new InMemoryServiceControlStore();
        var stateRepo = new InMemoryServiceStateRepository();
        var manager = new ServiceManager(controlStore, stateRepo);

        // Delayed auto-start in its real Windows configuration, and outside the protected
        // set, so the manager will actually let Aurum disable it.
        controlStore.AddService(new ServiceDefinition(
            "MapsBroker", "Downloaded Maps Manager", "Delayed automatic service",
            ServiceRunState.Running, ServiceStartMode.Automatic, true, 4321, []));

        await manager.DisableServiceAsync("MapsBroker");

        var persisted = await stateRepo.GetAsync("MapsBroker");
        NotNull(persisted, "Persisted service state was not saved.");
        True(persisted!.OriginalDelayedAutoStart, "The delayed auto-start flag was not captured.");

        controlStore.StartModeWrites.Clear();
        await manager.RevertServiceAsync("MapsBroker");

        var reverted = controlStore.GetService("MapsBroker")!;
        Equal(ServiceStartMode.Automatic, reverted.StartMode, "Expected start mode to revert to Automatic.");
        True(reverted.IsDelayedAutoStart, "Revert must restore the delayed auto-start flag.");

        var revertWrite = controlStore.StartModeWrites.Single();
        Equal(ServiceStartMode.Automatic, revertWrite.StartMode, "Revert requested the wrong start mode.");
        True(
            revertWrite.DelayedAutoStart == true,
            "Revert must pass the captured delayed auto-start flag to the SCM, not leave it unspecified.");
    }

    /// <summary>
    /// The service list hides the toggle for protected services, but that is a view-level
    /// check. The manager is the layer that actually writes to the SCM, so it enforces the
    /// same exclusion.
    /// </summary>
    private static async Task ServiceDisableRejectsProtectedServiceAsync()
    {
        var controlStore = new InMemoryServiceControlStore();
        var stateRepo = new InMemoryServiceStateRepository();
        var manager = new ServiceManager(controlStore, stateRepo);

        controlStore.AddService(new ServiceDefinition(
            "WinDefend", "Microsoft Defender Antivirus Service", "Antivirus",
            ServiceRunState.Running, ServiceStartMode.Automatic, false, 900, []));

        await ThrowsAsync<InvalidOperationException>(() => manager.DisableServiceAsync("WinDefend"));

        Equal(
            ServiceStartMode.Automatic,
            controlStore.GetService("WinDefend")!.StartMode,
            "A protected service must keep its start mode.");
        True(controlStore.StartModeWrites.Count == 0, "A protected service must not reach the SCM at all.");
        True(await stateRepo.GetAsync("WinDefend") is null, "A refused disable must not leave tracking state behind.");
    }

    /// <summary>
    /// Repair takes its service name from the persisted snapshot rather than from the
    /// click, and the snapshot lives in the user's profile where it is writable without
    /// elevation. Without the exclusion, an edited snapshot would be enough to have an
    /// elevated Aurum turn off Defender or the firewall.
    /// </summary>
    private static async Task ServiceRepairRejectsProtectedServiceAsync()
    {
        var controlStore = new InMemoryServiceControlStore();
        var stateRepo = new InMemoryServiceStateRepository();
        var manager = new ServiceManager(controlStore, stateRepo);

        controlStore.AddService(new ServiceDefinition(
            "MpsSvc", "Windows Defender Firewall", "Firewall",
            ServiceRunState.Running, ServiceStartMode.Automatic, false, 901, []));

        // Stands in for a snapshot file edited by a process running as the user without
        // elevation: Aurum itself would never have recorded a protected service.
        await stateRepo.SaveAsync(new PersistedServiceEntry(
            "MpsSvc", ServiceStartMode.Automatic, false, DateTimeOffset.UtcNow));

        var error = await ThrowsAsync<InvalidOperationException>(() => manager.RepairServiceAsync("MpsSvc"));

        True(
            error.Message.Contains("MpsSvc", StringComparison.Ordinal),
            $"Repair failed for an unrelated reason instead of rejecting the protected service: {error.Message}");
        Equal(
            ServiceStartMode.Automatic,
            controlStore.GetService("MpsSvc")!.StartMode,
            "A tampered snapshot must not be able to disable the firewall.");
        True(controlStore.StartModeWrites.Count == 0, "A refused repair must not reach the SCM at all.");
    }

    /// <summary>
    /// Aurum only ever sets a service to Disabled, so a service that is not disabled has
    /// nothing left to restore. Writing the recorded start mode regardless would make an
    /// edited snapshot a way to enable an arbitrary service through an elevated revert.
    /// </summary>
    private static async Task ServiceRevertOfEnabledServiceOnlyDropsTrackingAsync()
    {
        var controlStore = new InMemoryServiceControlStore();
        var stateRepo = new InMemoryServiceStateRepository();
        var manager = new ServiceManager(controlStore, stateRepo);

        controlStore.AddService(new ServiceDefinition(
            "RemoteRegistry", "Remote Registry", "Deliberately disabled by the administrator",
            ServiceRunState.Stopped, ServiceStartMode.Manual, false, 0, []));

        await stateRepo.SaveAsync(new PersistedServiceEntry(
            "RemoteRegistry", ServiceStartMode.Automatic, false, DateTimeOffset.UtcNow));

        await manager.RevertServiceAsync("RemoteRegistry");

        Equal(
            ServiceStartMode.Manual,
            controlStore.GetService("RemoteRegistry")!.StartMode,
            "Revert must not raise the start mode of a service Aurum had not disabled.");
        True(controlStore.StartModeWrites.Count == 0, "Revert must not reach the SCM when there is nothing to restore.");
        True(
            await stateRepo.GetAsync("RemoteRegistry") is null,
            "Revert must still drop the tracking entry, otherwise the service stays listed as managed forever.");
    }

    private static async Task ServiceGroupsAndEvaluationAsync()
    {
        var groups = BuiltInServiceGroups.All;
        True(groups.Count >= 5, "Expected at least 5 built-in service groups.");

        foreach (var group in groups)
        {
            True(!string.IsNullOrWhiteSpace(group.Id), "Group has empty Id.");
            True(!string.IsNullOrWhiteSpace(group.Name), $"Group '{group.Id}' has empty Name.");
            True(!string.IsNullOrWhiteSpace(group.Description), $"Group '{group.Id}' has empty Description.");
            True(!string.IsNullOrWhiteSpace(group.Impact), $"Group '{group.Id}' has empty Impact.");
            True(group.ServiceNames.Count > 0, $"Group '{group.Id}' has no services.");
        }

        var controlStore = new InMemoryServiceControlStore();
        var stateRepo = new InMemoryServiceStateRepository();
        var manager = new ServiceManager(controlStore, stateRepo);

        controlStore.AddService(new ServiceDefinition("Spooler", "Spooler", "", ServiceRunState.Running, ServiceStartMode.Automatic, false, 1, []));
        controlStore.AddService(new ServiceDefinition("Fax", "Fax", "", ServiceRunState.Stopped, ServiceStartMode.Manual, false, 0, []));

        var printGroup = groups.First(static g => g.Id == "print");
        var evalBefore = await manager.EvaluateGroupAsync(printGroup, controlStore.AllServices);
        True(!evalBefore.IsApplied, "Print group should not be applied before disabling.");

        await manager.DisableServiceAsync("Spooler");
        await manager.DisableServiceAsync("Fax");

        var evalAfter = await manager.EvaluateGroupAsync(printGroup, controlStore.AllServices);
        True(evalAfter.IsApplied, "Print group should be marked applied when all services are disabled by Aurum.");
        Equal(2, evalAfter.AppliedCount, "Applied count should match total services in group.");
    }

    private static async Task StorageTuningSnapshotsAndTogglesAsync()
    {
        var store = new InMemoryStorageTuningStore();
        var repo = new InMemoryStorageTuningStateRepository();
        var manager = new StorageTuningManager(store, repo, () => true);

        var initial = await manager.CaptureSnapshotAsync();
        True(!initial.Is8dot3Disabled, "Expected 8.3 names initially enabled.");
        True(!initial.IsLastAccessDisabled, "Expected LastAccess initially enabled.");
        True(!initial.IsHibernationDisabled, "Expected Hibernation initially enabled.");
        Equal(ServiceStartMode.Automatic, initial.SysMainStartMode, "Expected SysMain initially Automatic.");

        await manager.Toggle8dot3Async(true);
        var snap1 = await manager.CaptureSnapshotAsync();
        True(snap1.Is8dot3Disabled, "8.3 names must be disabled after toggle.");

        await manager.ToggleLastAccessAsync(true);
        var snap2 = await manager.CaptureSnapshotAsync();
        True(snap2.IsLastAccessDisabled, "LastAccess must be disabled after toggle.");

        await manager.ToggleHibernationAsync(true);
        var snap3 = await manager.CaptureSnapshotAsync();
        True(snap3.IsHibernationDisabled, "Hibernation must be disabled after toggle.");

        await manager.ToggleSysMainAsync(true);
        var snap4 = await manager.CaptureSnapshotAsync();
        Equal(ServiceStartMode.Disabled, snap4.SysMainStartMode, "SysMain must be disabled after toggle.");

        var savedState = await repo.GetAsync();
        NotNull(savedState, "Tuning repository must record initial state for rollback.");
        True(savedState!.Original8dot3Disabled == false, "Original 8.3 state preserved.");
        True(savedState!.OriginalLastAccessDisabled == false, "Original LastAccess state preserved.");
        True(savedState!.OriginalHibernationDisabled == false, "Original Hibernation state preserved.");

        var reverted = await manager.RevertAsync();
        True(reverted, "RevertAsync must succeed.");
        var snapAfterRevert = await manager.CaptureSnapshotAsync();
        True(!snapAfterRevert.Is8dot3Disabled, "8.3 names restored.");
        True(!snapAfterRevert.IsLastAccessDisabled, "LastAccess restored.");
        True(!snapAfterRevert.IsHibernationDisabled, "Hibernation restored.");
        Equal(ServiceStartMode.Automatic, snapAfterRevert.SysMainStartMode, "SysMain restored.");
        var stateAfterRevert = await repo.GetAsync();
        True(stateAfterRevert is null, "Storage state must be cleared after revert.");
    }

    private static async Task StorageTuningRequiresAdminAsync()
    {
        var store = new InMemoryStorageTuningStore();
        var repo = new InMemoryStorageTuningStateRepository();
        var manager = new StorageTuningManager(store, repo, () => false);

        await ThrowsAsync<UnauthorizedAccessException>(() => manager.Toggle8dot3Async(true));
        await ThrowsAsync<UnauthorizedAccessException>(() => manager.ToggleLastAccessAsync(true));
        await ThrowsAsync<UnauthorizedAccessException>(() => manager.ToggleHibernationAsync(true));
        await ThrowsAsync<UnauthorizedAccessException>(() => manager.ToggleSysMainAsync(true));
    }

    private static async Task NetworkTuningAppliesAndRevertsAsync()
    {
        var store = new InMemoryNetworkTuningStore();
        var repo = new InMemoryNetworkTuningStateRepository();
        var manager = new NetworkTuningManager(store, repo, () => true);

        var adapter = new NetworkAdapterInfo(
            Id: "{TEST-GUID}",
            Name: "Ethernet",
            Description: "Intel Ethernet Controller",
            InterfaceType: "Ethernet",
            OperationalStatus: "Up",
            SpeedBitsPerSecond: 1_000_000_000,
            Mtu: 1500,
            PhysicalAddress: "00-11-22-33-44-55",
            IPv4Addresses: new[] { "192.168.1.50" },
            IPv6Addresses: Array.Empty<string>(),
            Gateways: new[] { "192.168.1.1" },
            DnsServers: new[] { "192.168.1.1" },
            IsPrimary: true);

        var cloudflare = BuiltInDnsPresets.All.First(p => p.Id == "cloudflare");
        await manager.ApplyDnsPresetAsync(adapter, cloudflare);

        True(store.ConfiguredDns.ContainsKey("Ethernet"), "Ethernet must have configured DNS.");
        Equal("1.1.1.1", store.ConfiguredDns["Ethernet"][0], "Primary DNS must be Cloudflare 1.1.1.1.");
        Equal("1.0.0.1", store.ConfiguredDns["Ethernet"][1], "Secondary DNS must be Cloudflare 1.0.0.1.");
        Equal(1, store.FlushCount, "DNS Cache flush must be executed after applying preset.");

        var state = await repo.GetAsync("Ethernet");
        NotNull(state, "Rollback state must be recorded.");
        Equal("192.168.1.1", state!.OriginalDnsServers[0], "Original gateway DNS must be recorded for rollback.");

        await manager.RevertDnsAsync(adapter);
        Equal("192.168.1.1", store.ConfiguredDns["Ethernet"][0], "Original DNS must be restored upon revert.");
        var stateAfterRevert = await repo.GetAsync("Ethernet");
        True(stateAfterRevert is null, "State repository must be cleared after revert.");
    }

    private static async Task NetworkTuningRequiresAdminAsync()
    {
        var store = new InMemoryNetworkTuningStore();
        var repo = new InMemoryNetworkTuningStateRepository();
        var manager = new NetworkTuningManager(store, repo, () => false);

        var adapter = new NetworkAdapterInfo(
            Id: "{TEST-GUID}",
            Name: "Ethernet",
            Description: "Intel Ethernet Controller",
            InterfaceType: "Ethernet",
            OperationalStatus: "Up",
            SpeedBitsPerSecond: 1_000_000_000,
            Mtu: 1500,
            PhysicalAddress: "00-11-22-33-44-55",
            IPv4Addresses: new[] { "192.168.1.50" },
            IPv6Addresses: Array.Empty<string>(),
            Gateways: new[] { "192.168.1.1" },
            DnsServers: new[] { "192.168.1.1" },
            IsPrimary: true);

        var cloudflare = BuiltInDnsPresets.All.First(p => p.Id == "cloudflare");
        await ThrowsAsync<UnauthorizedAccessException>(() => manager.ApplyDnsPresetAsync(adapter, cloudflare));
        await ThrowsAsync<UnauthorizedAccessException>(() => manager.RevertDnsAsync(adapter));
        await ThrowsAsync<UnauthorizedAccessException>(() => manager.FlushDnsCacheAsync());
        await ThrowsAsync<UnauthorizedAccessException>(() => manager.SetTcpAutoTuningLevelAsync("normal"));
    }
}

internal sealed class InMemorySystemStore : ISystemStore
{
    private readonly Dictionary<RegistryTarget, RegistryValue> _values = [];
    private int _writeCount;

    public int? FailOnWriteNumber { get; init; }

    /// <summary>Shared with a repository fake so tests can assert the order of side effects.</summary>
    public List<string>? Journal { get; init; }

    public void Seed(RegistryTarget target, RegistryValue value) => _values[target] = value;

    public bool Contains(RegistryTarget target) => _values.ContainsKey(target);

    public RegistryValue Get(RegistryTarget target) => _values[target];

    public Task<RegistrySnapshot> ReadRegistryAsync(
        RegistryTarget target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_values.TryGetValue(target, out var value)
            ? new RegistrySnapshot(true, value)
            : RegistrySnapshot.Missing);
    }

    public Task WriteRegistryAsync(
        RegistryTarget target,
        RegistryValue value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Journal?.Add("registry-write");
        _writeCount++;
        if (_writeCount == FailOnWriteNumber)
        {
            throw new IOException("Injected write failure.");
        }

        _values[target] = value;
        return Task.CompletedTask;
    }

    public Task DeleteRegistryValueAsync(
        RegistryTarget target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _values.Remove(target);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryStateRepository : ITweakStateRepository
{
    private readonly Dictionary<string, PersistedTweakState> _states = new(StringComparer.Ordinal);

    /// <summary>Shared with a system-store fake so tests can assert the order of side effects.</summary>
    public List<string>? Journal { get; init; }

    public Task<PersistedTweakState?> GetAsync(string tweakId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_states.GetValueOrDefault(tweakId));
    }

    public Task<IReadOnlyList<PersistedTweakState>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<PersistedTweakState>>(_states.Values.ToArray());
    }

    public Task SaveAsync(PersistedTweakState state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Journal?.Add("snapshot-save");
        _states[state.TweakId] = state;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string tweakId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _states.Remove(tweakId);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryAuditJournal : IAuditJournal
{
    public List<AuditEntry> Entries { get; } = [];

    public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> ReadRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<AuditEntry>>(
            Entries.AsEnumerable().Reverse().Take(count).ToArray());
    }
}

internal sealed class InMemoryPowerPlanStore : IPowerPlanStore
{
    private readonly IReadOnlyList<PowerPlanInfo> _plans;

    public InMemoryPowerPlanStore(Guid originalId, Guid desiredId)
    {
        ActivePlanId = originalId;
        _plans =
        [
            new PowerPlanInfo(originalId, "Original"),
            new PowerPlanInfo(desiredId, "Desired"),
        ];
    }

    public Guid ActivePlanId { get; set; }

    /// <summary>Shared with a repository fake so tests can assert the order of side effects.</summary>
    public List<string>? Journal { get; init; }

    public Task<PowerPlanSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PowerPlanSnapshot(_plans, ActivePlanId));
    }

    public Task SetActiveAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Journal?.Add("plan-activate");
        if (!_plans.Any(plan => plan.Id == planId))
        {
            throw new InvalidOperationException("Unknown power plan.");
        }

        ActivePlanId = planId;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryPowerPlanStateRepository : IPowerPlanStateRepository
{
    public PersistedPowerPlanState? State { get; set; }
    public bool FailSave { get; init; }

    /// <summary>Shared with a store fake so tests can assert the order of side effects.</summary>
    public List<string>? Journal { get; init; }

    public Task<PersistedPowerPlanState?> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(State);
    }

    public Task SaveAsync(PersistedPowerPlanState state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Journal?.Add("state-save");
        if (FailSave)
        {
            throw new IOException("Injected power-plan persistence failure.");
        }

        State = state;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        State = null;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryStorageInventoryStore : IStorageInventoryStore
{
    private readonly IReadOnlyList<StorageVolumeInfo> _volumes;

    public InMemoryStorageInventoryStore(params StorageVolumeInfo[] volumes) => _volumes = volumes;

    public Task<IReadOnlyList<StorageVolumeInfo>> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_volumes);
    }
}

internal sealed class RecordingStorageOptimizer : IStorageOptimizer
{
    public string? LastRootPath { get; private set; }
    public StorageOperationKind? LastOperation { get; private set; }

    public Task<StorageOperationResult> RunAsync(
        string rootPath,
        StorageOperationKind operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastRootPath = rootPath;
        LastOperation = operation;
        return Task.FromResult(new StorageOperationResult(operation, rootPath, 0, "ok", DateTimeOffset.Now));
    }
}

internal sealed class InMemoryCoreParkingStore : ICoreParkingStore
{
    private readonly Dictionary<Guid, CoreParkingPlan> _plans = [];

    public InMemoryCoreParkingStore()
    {
        ActivePlanId = Guid.NewGuid();
        _plans[ActivePlanId] = new CoreParkingPlan(ActivePlanId, "Balanced", new CoreParkingSettings(10, 100, 10, 100));
    }

    public Guid ActivePlanId { get; private set; }
    public int PlanCount => _plans.Count;

    /// <summary>Shared with a repository fake so tests can assert the order of side effects.</summary>
    public List<string>? Journal { get; init; }
    public Task<CoreParkingPlan> CaptureActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult(_plans[ActivePlanId]);
    public Task<Guid> DuplicateAsync(Guid sourcePlanId, string newName, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        _plans[id] = new CoreParkingPlan(id, newName, _plans[sourcePlanId].Settings);
        return Task.FromResult(id);
    }
    public Task<CoreParkingSettings> ReadSettingsAsync(Guid planId, CancellationToken cancellationToken = default) => Task.FromResult(_plans[planId].Settings);
    public Task WriteSettingsAsync(Guid planId, CoreParkingSettings settings, CancellationToken cancellationToken = default)
    {
        Journal?.Add("plan-write-settings");
        _plans[planId] = _plans[planId] with { Settings = settings }; return Task.CompletedTask;
    }
    public Task SetActiveAsync(Guid planId, CancellationToken cancellationToken = default) { Journal?.Add("plan-activate"); ActivePlanId = planId; return Task.CompletedTask; }
    public Task<bool> ExistsAsync(Guid planId, CancellationToken cancellationToken = default) => Task.FromResult(_plans.ContainsKey(planId));
    public Task DeleteAsync(Guid planId, CancellationToken cancellationToken = default) { _plans.Remove(planId); return Task.CompletedTask; }
}

internal sealed class InMemoryCoreParkingStateRepository : ICoreParkingStateRepository
{
    public PersistedCoreParkingState? State { get; private set; }

    /// <summary>Shared with a store fake so tests can assert the order of side effects.</summary>
    public List<string>? Journal { get; init; }

    public Task<PersistedCoreParkingState?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(State);
    public Task SaveAsync(PersistedCoreParkingState state, CancellationToken cancellationToken = default) { Journal?.Add("state-save"); State = state; return Task.CompletedTask; }
    public Task RemoveAsync(CancellationToken cancellationToken = default) { State = null; return Task.CompletedTask; }
}

internal sealed class EmptyNetworkInventory : INetworkInventoryStore
{
    public Task<NetworkSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new NetworkSnapshot([], [], string.Empty, DateTimeOffset.Now));
}

internal sealed class RecordingNetworkProbe : INetworkProbe
{
    private readonly Queue<NetworkProbeSample> _samples;
    public RecordingNetworkProbe(IEnumerable<NetworkProbeSample> samples) => _samples = new Queue<NetworkProbeSample>(samples);
    public int SendCount { get; private set; }

    public Task<NetworkProbeSample> SendAsync(string target, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SendCount++;
        return Task.FromResult(_samples.Dequeue());
    }
}

internal sealed class InMemoryServiceControlStore : IServiceControlStore
{
    private readonly Dictionary<string, ServiceDefinition> _services = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<ServiceDefinition> AllServices => _services.Values;

    /// <summary>Every start-mode write, so tests can assert what was requested of the SCM.</summary>
    public List<(string ServiceName, ServiceStartMode StartMode, bool? DelayedAutoStart)> StartModeWrites { get; } = [];

    public void AddService(ServiceDefinition service) => _services[service.Name] = service;

    public ServiceDefinition? GetService(string serviceName) =>
        _services.GetValueOrDefault(serviceName);

    public void ChangeStartMode(string serviceName, ServiceStartMode startMode)
    {
        if (_services.TryGetValue(serviceName, out var current))
        {
            _services[serviceName] = current with { StartMode = startMode };
        }
    }

    public void SetState(string serviceName, ServiceRunState state)
    {
        if (_services.TryGetValue(serviceName, out var current))
        {
            _services[serviceName] = current with { State = state };
        }
    }

    public Task<ServiceDefinition?> GetServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
        Task.FromResult(_services.GetValueOrDefault(serviceName));

    public Task ChangeStartModeAsync(
        string serviceName,
        ServiceStartMode startMode,
        bool? delayedAutoStart = null,
        CancellationToken cancellationToken = default)
    {
        StartModeWrites.Add((serviceName, startMode, delayedAutoStart));
        ChangeStartMode(serviceName, startMode);
        if (delayedAutoStart is bool delayed && _services.TryGetValue(serviceName, out var current))
        {
            _services[serviceName] = current with { IsDelayedAutoStart = delayed };
        }

        return Task.CompletedTask;
    }

    public Task StopServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        SetState(serviceName, ServiceRunState.Stopped);
        return Task.CompletedTask;
    }

    public Task StartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        SetState(serviceName, ServiceRunState.Running);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryServiceStateRepository : IServiceStateRepository
{
    private readonly Dictionary<string, PersistedServiceEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<PersistedServiceEntry>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PersistedServiceEntry>>(_entries.Values.ToArray());

    public Task<PersistedServiceEntry?> GetAsync(string serviceName, CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries.GetValueOrDefault(serviceName));

    public Task SaveAsync(PersistedServiceEntry entry, CancellationToken cancellationToken = default)
    {
        _entries[entry.ServiceName] = entry;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        _entries.Remove(serviceName);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryStorageTuningStore : IStorageTuningStore
{
    public bool Is8dot3Disabled { get; set; }
    public bool IsLastAccessDisabled { get; set; }
    public bool IsHibernationDisabled { get; set; }
    public long HiberfilBytes { get; set; }
    public ServiceStartMode SysMainStartMode { get; set; } = ServiceStartMode.Automatic;
    public ServiceRunState SysMainState { get; set; } = ServiceRunState.Running;

    public Task<StorageTuningSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new StorageTuningSnapshot(
            Is8dot3Disabled,
            IsLastAccessDisabled,
            IsHibernationDisabled,
            HiberfilBytes,
            SysMainStartMode,
            SysMainState));

    public Task Set8dot3DisabledAsync(bool disabled, CancellationToken cancellationToken = default)
    {
        Is8dot3Disabled = disabled;
        return Task.CompletedTask;
    }

    public Task SetLastAccessDisabledAsync(bool disabled, CancellationToken cancellationToken = default)
    {
        IsLastAccessDisabled = disabled;
        return Task.CompletedTask;
    }

    public Task SetHibernationDisabledAsync(bool disabled, CancellationToken cancellationToken = default)
    {
        IsHibernationDisabled = disabled;
        HiberfilBytes = disabled ? 0 : 8L * 1024 * 1024 * 1024;
        return Task.CompletedTask;
    }

    public Task SetSysMainDisabledAsync(bool disabled, CancellationToken cancellationToken = default)
    {
        SysMainStartMode = disabled ? ServiceStartMode.Disabled : ServiceStartMode.Automatic;
        SysMainState = disabled ? ServiceRunState.Stopped : ServiceRunState.Running;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryStorageTuningStateRepository : IStorageTuningStateRepository
{
    private PersistedStorageTuningState? _state;

    public Task<PersistedStorageTuningState?> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_state);

    public Task SaveAsync(PersistedStorageTuningState state, CancellationToken cancellationToken = default)
    {
        _state = state;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(CancellationToken cancellationToken = default)
    {
        _state = null;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryNetworkTuningStore : INetworkTuningStore
{
    public Dictionary<string, IReadOnlyList<string>> ConfiguredDns { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int FlushCount { get; private set; }
    public string TcpAutoTuningLevel { get; set; } = "normal";
    public bool EcnCapability { get; set; }

    public Task SetDnsAsync(string adapterName, IReadOnlyList<string> dnsServers, CancellationToken cancellationToken = default)
    {
        ConfiguredDns[adapterName] = dnsServers;
        return Task.CompletedTask;
    }

    public Task ResetDnsToDhcpAsync(string adapterName, CancellationToken cancellationToken = default)
    {
        ConfiguredDns.Remove(adapterName);
        return Task.CompletedTask;
    }

    public Task FlushDnsCacheAsync(CancellationToken cancellationToken = default)
    {
        FlushCount++;
        return Task.CompletedTask;
    }

    public Task SetTcpAutoTuningLevelAsync(string level, CancellationToken cancellationToken = default)
    {
        TcpAutoTuningLevel = level;
        return Task.CompletedTask;
    }

    public Task SetEcnCapabilityAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        EcnCapability = enabled;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryNetworkTuningStateRepository : INetworkTuningStateRepository
{
    private readonly Dictionary<string, PersistedNetworkAdapterTuningState> _states = new(StringComparer.OrdinalIgnoreCase);

    public Task<PersistedNetworkAdapterTuningState?> GetAsync(string adapterName, CancellationToken cancellationToken = default) =>
        Task.FromResult(_states.GetValueOrDefault(adapterName));

    public Task SaveAsync(PersistedNetworkAdapterTuningState state, CancellationToken cancellationToken = default)
    {
        _states[state.AdapterName] = state;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string adapterName, CancellationToken cancellationToken = default)
    {
        _states.Remove(adapterName);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryMsiDeviceInventory : IMsiDeviceInventory
{
    public List<PciDeviceMsiInfo> Devices { get; set; } = new();

    /// <summary>When set, writes to this device instance id throw, simulating a driver refusal.</summary>
    public string? FailingDeviceInstanceId { get; init; }

    public Task<IReadOnlyList<PciDeviceMsiInfo>> CaptureAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PciDeviceMsiInfo>>(Devices.ToList());
    }

    public Task SetMsiPropertiesAsync(
        string deviceInstanceId,
        bool enableMsi,
        int messageNumberLimit,
        MsiDevicePriority priority,
        CancellationToken cancellationToken = default)
    {
        if (FailingDeviceInstanceId is not null &&
            string.Equals(FailingDeviceInstanceId, deviceInstanceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Injected MSI write failure for '{deviceInstanceId}'.");
        }

        var idx = Devices.FindIndex(d => string.Equals(d.DeviceInstanceId, deviceInstanceId, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            var old = Devices[idx];
            Devices[idx] = old with
            {
                IsMsiSupported = enableMsi,
                MessageNumberLimit = messageNumberLimit,
                Priority = priority
            };
        }
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryMsiStateRepository : IMsiStateRepository
{
    private MsiStateSnapshot? _snapshot;

    public Task<MsiStateSnapshot?> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_snapshot);

    public Task WriteAsync(MsiStateSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        _snapshot = snapshot;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _snapshot = null;
        return Task.CompletedTask;
    }
}

// Test Methods for MSI and Timer
internal static partial class Program
{
    /// <summary>
    /// A revert that cannot restore every device must keep its snapshot, otherwise the
    /// user loses the only record of the original interrupt configuration.
    /// </summary>
    private static async Task MsiRevertKeepsSnapshotOnFailureAsync()
    {
        const string gpuId = @"PCI\VEN_10DE&DEV_2484\01";
        const string netId = @"PCI\VEN_10EC&DEV_8168\02";

        var inventory = new InMemoryMsiDeviceInventory
        {
            Devices =
            [
                new(gpuId, "NVIDIA GeForce RTX 3070", MsiDeviceCategory.Gpu, "PCI bus 1", false, 1, MsiDevicePriority.Undefined, true, true),
                new(netId, "Realtek Gaming 2.5GbE", MsiDeviceCategory.Network, "PCI bus 2", false, 1, MsiDevicePriority.Undefined, true, true)
            ]
        };

        var repo = new InMemoryMsiStateRepository();
        await new MsiModeManager(inventory, repo, () => true).ApplyGamingPresetAsync();
        NotNull(await repo.ReadAsync(), "Applying the preset did not persist a snapshot.");

        // A second manager over the same repository, but with a device that now refuses writes.
        var failingInventory = new InMemoryMsiDeviceInventory
        {
            Devices = inventory.Devices,
            FailingDeviceInstanceId = netId
        };
        var failingManager = new MsiModeManager(failingInventory, repo, () => true);

        await ThrowsAsync<AggregateException>(() => failingManager.RevertAsync());

        NotNull(await repo.ReadAsync(), "A failed revert must not clear the rollback snapshot.");

        var gpu = failingInventory.Devices.First(d => d.DeviceInstanceId == gpuId);
        True(!gpu.IsMsiSupported, "The device that could be restored should have been reverted.");
    }

    private static async Task MsiModeAppliesGamingPresetAndRevertsAsync()
    {
        var inventory = new InMemoryMsiDeviceInventory
        {
            Devices = new List<PciDeviceMsiInfo>
            {
                new(@"PCI\VEN_10DE&DEV_2484\01", "NVIDIA GeForce RTX 3070", MsiDeviceCategory.Gpu, "PCI bus 1", false, 1, MsiDevicePriority.Undefined, true, true),
                new(@"PCI\VEN_10EC&DEV_8168\02", "Realtek Gaming 2.5GbE", MsiDeviceCategory.Network, "PCI bus 2", false, 1, MsiDevicePriority.Undefined, true, true),
                new(@"PCI\VEN_8086&DEV_43ED\03", "Intel USB 3.2 Controller", MsiDeviceCategory.Usb, "PCI bus 0", false, 1, MsiDevicePriority.Undefined, true, true),
                new(@"PCI\VEN_8086&DEV_7A27\04", "PCI-to-PCI Bridge", MsiDeviceCategory.System, "PCI bus 0", false, 1, MsiDevicePriority.Undefined, true, false)
            }
        };

        var repo = new InMemoryMsiStateRepository();
        var manager = new MsiModeManager(inventory, repo, () => true);

        var updatedCount = await manager.ApplyGamingPresetAsync();
        if (updatedCount != 3)
        {
            throw new Exception($"Ожидалось оптимизировать 3 устройства, но было {updatedCount}");
        }

        var devicesAfter = await inventory.CaptureAsync();
        var gpu = devicesAfter.First(d => d.Category == MsiDeviceCategory.Gpu);
        var net = devicesAfter.First(d => d.Category == MsiDeviceCategory.Network);
        var usb = devicesAfter.First(d => d.Category == MsiDeviceCategory.Usb);
        var sys = devicesAfter.First(d => d.Category == MsiDeviceCategory.System);

        if (!gpu.IsMsiSupported || gpu.Priority != MsiDevicePriority.High)
            throw new Exception("GPU должен быть в режиме MSI с высоким приоритетом (High).");

        if (!net.IsMsiSupported || net.Priority != MsiDevicePriority.High)
            throw new Exception("Сетевая карта должна быть в режиме MSI с высоким приоритетом (High).");

        if (!usb.IsMsiSupported || usb.Priority != MsiDevicePriority.Normal)
            throw new Exception("USB должен быть в режиме MSI с нормальным приоритетом (Normal).");

        if (sys.IsMsiSupported)
            throw new Exception("Системный мост не должен был измениться.");

        // Проверяем откат
        await manager.RevertAsync();
        var devicesReverted = await inventory.CaptureAsync();
        var gpuReverted = devicesReverted.First(d => d.Category == MsiDeviceCategory.Gpu);

        if (gpuReverted.IsMsiSupported || gpuReverted.Priority != MsiDevicePriority.Undefined)
            throw new Exception("Откат должен был восстановить IsMsiSupported=false и Priority=Undefined.");
    }

    private static async Task MsiModeRequiresAdminAsync()
    {
        var inventory = new InMemoryMsiDeviceInventory();
        var repo = new InMemoryMsiStateRepository();
        var manager = new MsiModeManager(inventory, repo, () => false);

        try
        {
            await manager.ApplyGamingPresetAsync();
            throw new Exception("Ожидалось исключение из-за отсутствия прав администратора.");
        }
        catch (InvalidOperationException)
        {
            // Ожидаемо
        }
    }

    private static Task SystemTimerResolutionCalculationsAsync()
    {
        var info = new TimerResolutionInfo(5000, 156250, 5000, true);

        if (Math.Abs(info.CurrentMs - 0.5) > 0.001)
            throw new Exception($"Ожидалось 0.5 мс, но получено {info.CurrentMs}");

        if (Math.Abs(info.FrequencyHz - 2000.0) > 0.1)
            throw new Exception($"Ожидалось 2000 Гц, но получено {info.FrequencyHz}");

        if (!info.FormattedCurrent.Contains("0,500 мс") && !info.FormattedCurrent.Contains("0.500 мс"))
            throw new Exception($"Неверное форматирование: {info.FormattedCurrent}");

        return Task.CompletedTask;
    }

    private static async Task WindowsPciInventoryIsReadableAsync()
    {
        var inventory = new WindowsPciDeviceInventory();
        var devices = await inventory.CaptureAsync();

        if (devices == null)
        {
            throw new Exception("WindowsPciDeviceInventory вернул null.");
        }
    }

    private static Task MsiCategoryIconsAndLabelsAreValidAsync()
    {
        var categories = new[]
        {
            (MsiDeviceCategory.Gpu, "Видеокарта (GPU)"),
            (MsiDeviceCategory.Network, "Сетевой адаптер (LAN/Wi-Fi)"),
            (MsiDeviceCategory.Audio, "Звуковая карта (Audio)"),
            (MsiDeviceCategory.Usb, "USB Контроллер"),
            (MsiDeviceCategory.Storage, "Дисковый контроллер (NVMe/SATA)"),
            (MsiDeviceCategory.System, "Системное устройство")
        };

        foreach (var (cat, expected) in categories)
        {
            var info = new PciDeviceMsiInfo("TEST", "Device", cat, "Bus", true, 1, MsiDevicePriority.High, true, true);
            if (info.Category != cat)
                throw new Exception($"Не совпала категория для {cat}");
        }

        return Task.CompletedTask;
    }

    private static Task SystemTimerResolutionEdgeValuesAsync()
    {
        // 1.0 ms test: Minimum (15.625ms), Maximum (0.5ms), Current (1.0ms = 10000 100ns units)
        var info10 = new TimerResolutionInfo(156250, 5000, 10000, true);
        if (Math.Abs(info10.CurrentMs - 1.0) > 0.001)
            throw new Exception($"Ожидалось 1.0 мс, но получено {info10.CurrentMs}");
        if (Math.Abs(info10.FrequencyHz - 1000.0) > 0.1)
            throw new Exception($"Ожидалось 1000 Гц, но получено {info10.FrequencyHz}");

        // 15.625 ms default test
        var infoDefault = new TimerResolutionInfo(156250, 5000, 156250, false);
        if (Math.Abs(infoDefault.CurrentMs - 15.625) > 0.001)
            throw new Exception($"Ожидалось 15.625 мс, но получено {infoDefault.CurrentMs}");
        if (Math.Abs(infoDefault.FrequencyHz - 64.0) > 0.1)
            throw new Exception($"Ожидалось 64 Гц, но получено {infoDefault.FrequencyHz}");

        return Task.CompletedTask;
    }

    private static Task AllReferencedTweakIdsExistInCatalogAsync()
    {
        var catalogIds = BuiltInTweakCatalog.All.Select(t => t.Id).ToHashSet();
        string[] requiredIds =
        [
            TweakIds.Kernel.Win32PrioritySeparation,
            TweakIds.Kernel.DisablePagingExecutive,
            TweakIds.Kernel.SystemResponsiveness,
            TweakIds.Network.DisableThrottling,
            TweakIds.Gaming.DisableBackgroundCapture,
            TweakIds.Gaming.DisableXboxGameBar,
            TweakIds.Explorer.ClassicContextMenu,
            TweakIds.Privacy.DisableAdvertisingId,
            TweakIds.Privacy.DisableFeedbackRequests,
            TweakIds.Privacy.DisableTailoredExperiences,
            TweakIds.Privacy.DisableSearchWebResults
        ];

        foreach (var id in requiredIds)
        {
            if (!catalogIds.Contains(id))
            {
                throw new Exception($"Не найден твик '{id}' в BuiltInTweakCatalog.All.");
            }
        }

        return Task.CompletedTask;
    }

    private static async Task TweakEngineWritesAuditJournalAsync()
    {
        var store = new InMemorySystemStore();
        store.Seed(FirstTarget, RegistryValue.String("original"));
        var repository = new InMemoryStateRepository();
        var journal = new InMemoryAuditJournal();
        var engine = new TweakEngine(store, repository, journal);
        var definition = CreateDefinition();

        await engine.ApplyAsync(definition);
        await engine.RevertAsync(definition);

        Equal(2, journal.Entries.Count, "Apply and revert should each write one audit entry.");
        Equal(AuditAction.Applied, journal.Entries[0].Action, "First entry should be apply.");
        True(journal.Entries[0].Succeeded, "Successful apply was recorded as a failure.");
        Equal(AuditAction.Reverted, journal.Entries[1].Action, "Second entry should be revert.");

        var failingStore = new InMemorySystemStore { FailOnWriteNumber = 2 };
        failingStore.Seed(FirstTarget, RegistryValue.String("original"));
        var failingEngine = new TweakEngine(failingStore, new InMemoryStateRepository(), journal);
        await ThrowsAsync<TweakTransactionException>(() => failingEngine.ApplyAsync(definition));
        Equal(AuditAction.Failed, journal.Entries[^1].Action, "Failed apply was not recorded.");
        True(!journal.Entries[^1].Succeeded, "Failed apply was recorded as success.");
    }

    private static async Task JsonlAuditJournalRoundTripsAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "AurumTests", $"audit-{Guid.NewGuid():N}.jsonl");
        try
        {
            var journal = new JsonlAuditJournal(path);
            await journal.AppendAsync(new AuditEntry(
                DateTimeOffset.UtcNow.AddMinutes(-1),
                "tweak",
                "older",
                AuditAction.Applied,
                true,
                "first"));
            await journal.AppendAsync(new AuditEntry(
                DateTimeOffset.UtcNow,
                "tweak",
                "newer",
                AuditAction.Reverted,
                true,
                "second"));

            var recent = await journal.ReadRecentAsync(1);
            Equal(1, recent.Count, "ReadRecentAsync did not honor the count.");
            Equal("newer", recent[0].Subject, "Newest entry should be returned first.");

            var both = await journal.ReadRecentAsync(10);
            Equal(2, both.Count, "Both appended entries should be readable.");
            Equal("newer", both[0].Subject, "Descending order was lost.");
            Equal("older", both[1].Subject, "Older entry should follow.");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}






