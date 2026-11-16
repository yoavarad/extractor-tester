# Graph Report - 171  (2026-11-16)

## Corpus Check
- 233 files · ~168,233 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 13 file(s) not represented in the graph (top: (none) 5, .jsonl 4, .props 1)

## Summary
- 2720 nodes · 6520 edges · 134 communities (103 shown, 31 thin omitted)
- Extraction: 93% EXTRACTED · 7% INFERRED · 0% AMBIGUOUS · INFERRED: 468 edges (avg confidence: 0.9)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `7951916a`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- ExtractorTester.Models
- List
- pathlib
- GoldenTests
- models.py
- Dictionary
- pytest
- ConfigTests
- test_loader.py
- compare_text
- test_decode.py
- CompareConfig
- compare_item
- write_item
- RunnerTests
- LoaderTests
- build
- gen_compare_fixtures.py
- FileRecord
- test_output.py
- Fact
- with
- test_tiers.py
- generator.py
- .add_pair
- test_aggregate.py
- test_matcher.py
- ErrorCode
- .CompareAll
- steps
- ErrorCode
- Theory
- ydk-sync.yml
- Text
- Scanner
- PairFacts
- ydk-pr-body.yml
- Json
- family
- Report
- tasks
- tasks
- .CompareBuffered
- FieldDifferTests
- PyJson
- test_compare_fields.py
- corpus.py
- CompareConfig
- OutputTests
- Example
- TextFacts
- ItemComparerTests
- ydk-spec-integrity.yml
- undefined_keys
- test_pool.py
- channel.py
- Slot
- FamilyAggregate
- AggregateTests
- .Run
- FieldDiff
- test_render.py
- .CompareAll
- ydk-delete-merged-branch.yml
- WorkerMessage
- compare
- sys
- checks.py
- test_golden.py
- Exception
- IEnumerable
- test_models.py
- ReadOnlySpan
- bench_run
- ADR-009: Port to C# .NET 8
- ExtractorTester.Tests.csproj
- ChannelHooks
- SequenceMatcher
- test_runner_progress.py
- gen_aggregate_fixtures.py
- RenderTests
- corpus_dir
- Aggregator
- MetricResult
- ItemResult
- Aggregator
- test_outputs_identical_for_one_and_four_workers
- aggregate_util.py
- Slowest
- Matching
- test_channel.py
- test_cli.py
- ADR-003: Hybrid full / buffered text comparison
- ADR-006: Tech stack and scale design
- README.md
- Initial plan summary (Stage 02)
- ErrorRecord
- ADR-001: Match files across pipelines by tree path
- ADR-002: Comparison metrics
- ADR-004: Report as JSON + self-contained HTML
- ADR-005: Single compare config; same schema both sides
- ADR-007: Built-in profiling
- ADR-008: No ignition pack; Stage 01.5 skipped
- Project Rules
- Architecture
- Metrics
- Overview
- _Rev
- JSON report structure
- Errors and edge cases
- Outputs
- Slowest
- Configuration reference
- .NET port
- Scope
- Testing strategy
- .Stats
- Configuration
- task.md
- PULL_REQUEST_TEMPLATE.md
- main
- Benchmark: full-text Indel similarity (rapidfuzz)
- epic.md
- test_write_item_round_trips_text
- check-task-complete.sh
- story.md
- extractor-tester

## God Nodes (most connected - your core abstractions)
1. `ErrorCode` - 57 edges
2. `CompareConfig` - 56 edges
3. `CompareConfig` - 46 edges
4. `PairFacts` - 44 edges
5. `RunnerTests` - 44 edges
6. `write_item()` - 41 edges
7. `Aggregator` - 39 edges
8. `FileRecord` - 37 edges
9. `MatchingTests` - 35 edges
10. `Aggregator` - 34 edges

## Surprising Connections (you probably didn't know these)
- `Family entry` --references--> `family()`  [INFERRED]
  docs/report-format.md → src/extractor_tester/families.py
- `run_stats` --references--> `render()`  [INFERRED]
  docs/report-format.md → src/extractor_tester/render.py
- `Decision` --references--> `ChannelHooks`  [INFERRED]
  docs/adrs/009-dotnet-port.md → dotnet/src/ExtractorTester/Channel.cs
- `Decision` --references--> `Indel`  [INFERRED]
  docs/adrs/009-dotnet-port.md → dotnet/src/ExtractorTester/Indel.cs
- `Decision` --references--> `PoolOptions`  [INFERRED]
  docs/adrs/009-dotnet-port.md → dotnet/src/ExtractorTester/Pool.cs

## Import Cycles
- None detected.

## Communities (134 total, 31 thin omitted)

### Community 0 - "ExtractorTester.Models"
Cohesion: 0.05
Nodes (23): ExtractorTester.Pool, ExtractorTester.Profiling, ExtractorTester.Loader, ExtractorTester.Render, ExtractorTester.Cli, ExtractorTester.Config, ExtractorTester.Aggregate, ExtractorTester.Channel (+15 more)

### Community 1 - "List"
Cohesion: 0.06
Nodes (43): ItemComparer, IMatchTier, Name, Matcher, NameTier, Name, TextHashTier, Name (+35 more)

### Community 2 - "pathlib"
Cohesion: 0.04
Nodes (20): items_writer(), JsonlWriter, OutputError, pairs_writer(), prepare_out(), _write_failed(), write_report(), compare_all() (+12 more)

### Community 3 - "GoldenTests"
Cohesion: 0.07
Nodes (7): GoldenChecks, GoldenCorpus, GoldenTests, Interrupt, ProcessInformation, SecurityAttributes, StartupInfo

### Community 4 - "models.py"
Cohesion: 0.06
Nodes (29): _blank(), _facts(), _failed(), _file_errors(), _hints(), _metadata_diffs(), match_item(), _one_sided() (+21 more)

### Community 5 - "Dictionary"
Cohesion: 0.08
Nodes (9): FieldFlattener, FlattenCollisionException, AutoEscapeContext, PyText, TreeBuilder, TreeException, Code, FieldsTests (+1 more)

### Community 6 - "pytest"
Cohesion: 0.06
Nodes (26): ConfigError, load_config(), _load_families(), _read_yaml(), cfg(), test_bad_yaml(), test_family_map_not_an_input_key(), test_family_relative_to_config_not_cwd() (+18 more)

### Community 7 - "ConfigTests"
Cohesion: 0.06
Nodes (13): ConfigException, ConfigPath, Reason, ConfigLoader, Field, Kind, Float, Int (+5 more)

### Community 8 - "test_loader.py"
Cohesion: 0.07
Nodes (23): discover(), DiscoveredItem, Discovery, _invalid(), load_item(), LoaderError, _scan(), cfg() (+15 more)

### Community 9 - "compare_text"
Cohesion: 0.07
Nodes (28): compare_text(), _cfg(), _edit(), _lines(), test_buffered_close_to_full_on_line_edits(), test_no_line_breaks_is_cut_and_counted(), test_one_side_without_text(), test_oversized_replaced_run_compared_block_by_block() (+20 more)

### Community 10 - "test_decode.py"
Cohesion: 0.07
Nodes (21): main(), _count_handler(), decode_text(), InvalidBase64Error, normalize(), text_hash(), _b64(), test_counter_resets_between_calls() (+13 more)

### Community 11 - "CompareConfig"
Cohesion: 0.08
Nodes (14): compare_buffered(), _lcs(), split_blocks(), diff(), _equal(), _json_type(), _snip(), _buffered() (+6 more)

### Community 12 - "compare_item"
Cohesion: 0.11
Nodes (17): compare_item(), f(), sides(), test_exception_becomes_item_processing_failed(), test_facts_never_contain_full_text(), test_field_filled_and_emptied_hints(), test_file_level_error_attached_to_its_pair(), test_invalid_base64_skips_text_but_keeps_field_diffs() (+9 more)

### Community 13 - "write_item"
Cohesion: 0.11
Nodes (18): write_item(), folders(), invoke(), item_files(), _html_run(), test_empty_folders_are_a_valid_run(), test_file_mode_reports_one_item(), test_folders_of_three_items_write_all_outputs() (+10 more)

### Community 15 - "LoaderTests"
Cohesion: 0.14
Nodes (5): ItemLoader, JsonRecursionException, LoaderException, Code, LoaderTests

### Community 16 - "build"
Cohesion: 0.14
Nodes (21): build(), _bad(), f(), item(), test_deep_chain(), test_duplicate_file_id(), test_matcher_does_not_pair_broken_files_by_text_hash(), test_matcher_pairs_renamed_file_by_text_hash() (+13 more)

### Community 17 - "gen_compare_fixtures.py"
Cohesion: 0.10
Nodes (16): field_case(), field_cases(), item_case(), item_cases(), raw_sides(), main(), raw_item(), text_case() (+8 more)

### Community 18 - "FileRecord"
Cohesion: 0.10
Nodes (10): default_tiers(), _hash_key(), MatchTier, NameTier, _pair_group(), _pair_unique(), TextHashTier, TypeFamilyTier (+2 more)

### Community 19 - "test_output.py"
Cohesion: 0.09
Nodes (13): _mr(), _pair(), _report(), test_items_jsonl(), test_pairs_jsonl_one_per_line(), test_prepare_creates_missing(), test_prepare_empty_ok(), test_prepare_non_empty_overwrite() (+5 more)

### Community 21 - "with"
Cohesion: 0.08
Nodes (31): concurrency, cancel-in-progress, group, name, runs-on, steps, jobs, dotnet (+23 more)

### Community 22 - "test_tiers.py"
Cohesion: 0.10
Nodes (15): ids(), rec(), test_empty_names_never_pair(), test_extra_same_name_files_pass_on_in_order(), test_hash_tie_break_requires_same_family(), test_names_are_case_sensitive(), test_same_name_empty_text_pairs_by_position(), test_same_name_text_hash_breaks_tie_before_position() (+7 more)

### Community 23 - "generator.py"
Cohesion: 0.10
Nodes (13): main(), mutate(), rand_str(), main(), mutate(), edit(), generate(), make_text() (+5 more)

### Community 24 - ".add_pair"
Cohesion: 0.12
Nodes (5): _Counter, _Family, Lowest, Example, test_lowest_keeps_cap_lowest()

### Community 25 - "test_aggregate.py"
Cohesion: 0.15
Nodes (15): _cfg(), _item(), _pair(), _run(), test_cut_line_warning_and_hints(), test_decode_errors_summed(), test_deterministic(), test_errors_by_code() (+7 more)

### Community 26 - "test_matcher.py"
Cohesion: 0.12
Nodes (12): FamilyFake, item(), rec(), summary(), test_children_pair_and_leftovers_have_no_candidate(), test_custom_tier_list_labels_pairs(), test_default_tiers_order(), test_descendants_of_unpaired_parent_are_parent_unpaired() (+4 more)

### Community 27 - "ErrorCode"
Cohesion: 0.07
Nodes (24): ErrorCode, DUPLICATE_FILE_ID, EMPTY_INPUT, INPUT_NOT_FOUND, INTERNAL_FAILURE, INVALID_BASE64, INVALID_CONFIG, INVALID_JSON (+16 more)

### Community 28 - ".CompareAll"
Cohesion: 0.16
Nodes (5): PoolOptions, Hooks, StartupTimeoutSeconds, WorkerEnvironment, PoolTests

### Community 29 - "steps"
Cohesion: 0.08
Nodes (26): name, runs-on, steps, name, runs-on, steps, concurrency, cancel-in-progress (+18 more)

### Community 30 - "ErrorCode"
Cohesion: 0.12
Nodes (10): flatten(), ErrorCode, _check_unreachable(), _esc(), _find_root(), _name(), _record(), _str() (+2 more)

### Community 32 - "ydk-sync.yml"
Cohesion: 0.08
Nodes (24): concurrency, cancel-in-progress, group, env, GH_REPO, GH_TOKEN, YDK_VERSION, jobs (+16 more)

### Community 33 - "Text"
Cohesion: 0.16
Nodes (3): InvalidBase64Exception, Text, DecodeParityTests

### Community 34 - "Scanner"
Cohesion: 0.17
Nodes (5): PyDecodeException, PyJsonDecoder, Scanner, StopIteration, Idx

### Community 35 - "PairFacts"
Cohesion: 0.08
Nodes (24): PairFacts, ChildrenNew, ChildrenOld, ChildrenOnlyNew, ChildrenOnlyOld, DecodeErrorsNew, DecodeErrorsOld, Errors (+16 more)

### Community 36 - "ydk-pr-body.yml"
Cohesion: 0.08
Nodes (23): concurrency, cancel-in-progress, group, env, BASE_REF, PR_BODY, YDK_VERSION, jobs (+15 more)

### Community 37 - "Json"
Cohesion: 0.16
Nodes (5): item_errors(), read_jsonl(), run_with_workers(), test_crashed_worker_marks_only_its_item(), test_slow_item_times_out_and_others_are_compared()

### Community 38 - "family"
Cohesion: 0.11
Nodes (8): family(), mime(), cfg(), test_family_listed(), test_family_unknown(), test_family_unlisted_is_own(), test_mime_join(), test_mime_missing()

### Community 39 - "Report"
Cohesion: 0.10
Nodes (15): Report, Config, Errors, Families, Inputs, ItemFields, ItemsOnlyNew, ItemsOnlyOld (+7 more)

### Community 40 - "tasks"
Cohesion: 0.09
Nodes (22): epics, description, id, spec_refs, title, stories, acceptance, component_refs (+14 more)

### Community 41 - "tasks"
Cohesion: 0.09
Nodes (22): epics, description, id, spec_refs, title, stories, acceptance, component_refs (+14 more)

### Community 42 - ".CompareBuffered"
Cohesion: 0.19
Nodes (3): BufferedComparer, BufferedFacts, TextComparer

### Community 44 - "PyJson"
Cohesion: 0.17
Nodes (3): PyAnyConverter, PyJson, TestItems

### Community 45 - "test_compare_fields.py"
Cohesion: 0.12
Nodes (11): is_empty(), by_path(), cfg(), test_ignored_and_structural_skipped_sorted_groups(), test_is_empty(), test_lists_whole_and_nested_numbers(), test_nan_same(), test_null_is_value() (+3 more)

### Community 46 - "corpus.py"
Cohesion: 0.16
Nodes (9): build(), export_dotnet(), _item(), normalize(), normalized_outputs(), regenerate(), _root(), run() (+1 more)

### Community 47 - "CompareConfig"
Cohesion: 0.11
Nodes (17): CompareConfig, ContainmentPct, CutLinesWarnPct, ExamplesPerResult, FullCompareMaxChars, GenericFields, IgnoreFields, ItemTimeoutSeconds (+9 more)

### Community 48 - "OutputTests"
Cohesion: 0.20
Nodes (3): OutputError, Code, OutputTests

### Community 49 - "Example"
Cohesion: 0.20
Nodes (11): ExampleCounter, FamilyState, Lowest, Example, ItemId, NewValue, Note, OldValue (+3 more)

### Community 50 - "TextFacts"
Cohesion: 0.10
Nodes (20): TextFacts, EditSimilarity, Jaccard, LengthDelta, LinesCut, LinesCutPct, LinesTotal, Method (+12 more)

### Community 52 - "ydk-spec-integrity.yml"
Cohesion: 0.10
Nodes (19): name, runs-on, steps, concurrency, cancel-in-progress, group, env, YDK_VERSION (+11 more)

### Community 53 - "undefined_keys"
Cohesion: 0.16
Nodes (10): field_names(), _lists_named(), metric_results(), missing_examples(), undefined_keys(), _report(), test_example_presence_check_fails_on_emptied_list(), test_golden_report_has_examples_on_every_nonzero_result() (+2 more)

### Community 54 - "test_pool.py"
Cohesion: 0.18
Nodes (7): three_items(), _compare_all(), test_result_already_received_beats_timeout(), test_stalled_sender_does_not_block_other_workers(), test_worker_hung_at_startup_is_replaced(), test_worker_killed_mid_write_marks_only_its_item(), test_workers_dying_before_taking_items_end_the_run()

### Community 55 - "channel.py"
Cohesion: 0.16
Nodes (6): deliver(), open_slot(), _reader(), recv(), Slot, clock()

### Community 56 - "Slot"
Cohesion: 0.11
Nodes (14): Slot, Broken, BusySince, Conn, DeadSince, Inbox, Msgs, Proc (+6 more)

### Community 57 - "FamilyAggregate"
Cohesion: 0.11
Nodes (19): FamilyAggregate, CutLinesWarning, DecodeErrors, EditSimilarity, Family, FamilyChanged, Fields, FileErrors (+11 more)

### Community 59 - ".Run"
Cohesion: 0.16
Nodes (4): CliApp, CompareRunner, PyPath, SmokeTests

### Community 60 - "FieldDiff"
Cohesion: 0.11
Nodes (16): FieldDiff, Group, NewType, NewValue, OldType, OldValue, Path, Status (+8 more)

### Community 61 - "test_render.py"
Cohesion: 0.16
Nodes (8): _html(), test_golden(), test_import_graph(), test_no_external_urls(), test_script_escaped(), test_similarity_label(), walk(), test_compare_help_lists_arguments()

### Community 62 - ".CompareAll"
Cohesion: 0.18
Nodes (7): DiscoveredItem, Discovery, ItemMessage, WorkerConfig, WorkerPool, WorkerStats, Replaced

### Community 63 - "ydk-delete-merged-branch.yml"
Cohesion: 0.12
Nodes (16): if, runs-on, steps, BRANCH, GH_TOKEN, jobs, delete-branch, name (+8 more)

### Community 64 - "WorkerMessage"
Cohesion: 0.24
Nodes (3): WorkerChannel, WorkerMessage, ChannelTests

### Community 65 - "compare"
Cohesion: 0.13
Nodes (7): Documentation, extractor-tester, Input format, Installation and quick start, Output, Process, compare()

### Community 66 - "sys"
Cohesion: 0.17
Nodes (5): _read_stdin(), cases(), main(), match_case(), tier_case()

### Community 67 - "checks.py"
Cohesion: 0.16
Nodes (6): _key_forms(), _key_patterns(), _placeholder_patterns(), tool_error_codes(), undefined_keys_message(), test_undefined_key_message_names_key_and_expected_forms()

### Community 68 - "test_golden.py"
Cohesion: 0.17
Nodes (6): _keys_report(), _result(), test_empty_tool_error_folder_is_a_clear_error(), test_error_code_placeholder_matches_only_known_codes(), test_field_placeholder_matches_field_names_in_report(), test_field_with_colon_and_extra_suffix_is_a_field_name_only_if_listed()

### Community 69 - "Exception"
Cohesion: 0.22
Nodes (4): JsonIntDigitsException, JsonlWriter, Path, ReportWriter

### Community 71 - "test_models.py"
Cohesion: 0.17
Nodes (6): test_fields_match_manifests(), _sample(), test_dump_stable_and_round_trip(), test_error_codes_match_manifests(), test_fields_match_manifest(), test_frozen()

### Community 73 - "bench_run"
Cohesion: 0.13
Nodes (7): bench_run(), sample(), BenchRun, test_html_size(), test_peak_memory(), test_run_time_100k(), _tree_rss()

### Community 74 - "ADR-009: Port to C# .NET 8"
Cohesion: 0.14
Nodes (9): ADR-009: Port to C# .NET 8, Alternatives considered, Consequences, Context, Decision, Status, _common_prefix(), _first_equal() (+1 more)

### Community 75 - "ExtractorTester.Tests.csproj"
Cohesion: 0.15
Nodes (9): Microsoft.NET.Sdk, Microsoft.NET.Sdk, coverlet.collector (6.0.0), Microsoft.NET.Test.Sdk (17.8.0), Scriban (7.5.3), System.CommandLine (2.0.0), xunit (2.5.3), xunit.runner.visualstudio (2.5.3) (+1 more)

### Community 76 - "ChannelHooks"
Cohesion: 0.15
Nodes (10): ChannelHooks, BeforeDispose, Clock, Deliver, ReaderJoinMilliseconds, SendGraceSeconds, RunOptions, BeforeFinish (+2 more)

### Community 77 - "SequenceMatcher"
Cohesion: 0.35
Nodes (4): MatchingBlock, Opcode, SequenceMatcher, SequenceMatcherTests

### Community 78 - "test_runner_progress.py"
Cohesion: 0.21
Nodes (4): test_matching_does_not_import_compare_or_render(), cfg(), test_ctrl_c_exits_130_without_report(), test_progress_line_on_stderr()

### Community 79 - "gen_aggregate_fixtures.py"
Cohesion: 0.24
Nodes (5): build_scenarios(), build_util(), main(), scenario(), test_module_imports()

### Community 81 - "corpus_dir"
Cohesion: 0.17
Nodes (4): corpus_dir(), test_dotnet_golden_input_is_current(), test_golden_outputs_match(), test_golden_outputs_match_from_long_temp_path()

### Community 84 - "MetricResult"
Cohesion: 0.24
Nodes (6): MetricResult, Count, Denominator, Examples, Key, Rate

### Community 85 - "ItemResult"
Cohesion: 0.18
Nodes (11): ItemResult, Errors, FilesChanged, FilesIdentical, FilesOnlyNew, FilesOnlyOld, ItemId, MetadataDiffs (+3 more)

### Community 87 - "test_outputs_identical_for_one_and_four_workers"
Cohesion: 0.20
Nodes (5): _jsonl_untimed(), test_outputs_identical_for_one_and_four_workers(), test_single_worker_runs_in_separate_process(), test_workers_zero_exits_2(), _untimed()

### Community 90 - "Matching"
Cohesion: 0.22
Nodes (8): Matching, The tree, The walk, Tier list as the single source of matching rules, Tiers, Type family, Why matching is needed, Worked example

### Community 91 - "test_channel.py"
Cohesion: 0.28
Nodes (3): _slot(), test_corrupt_payload_marks_broken(), test_reader_queues_messages_then_marks_broken_on_eof()

### Community 92 - "test_cli.py"
Cohesion: 0.33
Nodes (3): setup(), test_missing_input_exits_1(), test_unknown_option_exits_2()

### Community 93 - "ADR-003: Hybrid full / buffered text comparison"
Cohesion: 0.25
Nodes (7): ADR-003: Hybrid full / buffered text comparison, Alternatives considered, Amendment (2026-10-26, spec writing), Consequences, Context, Decision, Status

### Community 94 - "ADR-006: Tech stack and scale design"
Cohesion: 0.25
Nodes (7): ADR-006: Tech stack and scale design, Alternatives considered, Amendment (2026-10-26): per-item timeout, Consequences, Context, Decision, Status

### Community 96 - "Initial plan summary (Stage 02)"
Cohesion: 0.25
Nodes (7): Checks, Config, Demo increments, Epics, First ready task, Initial plan summary (Stage 02), Schedule (`ydk task plan-waves --agents 3`)

### Community 97 - "ErrorRecord"
Cohesion: 0.25
Nodes (8): ErrorRecord, Code, FilePath, ItemId, Message, Scope, Severity, Side

### Community 98 - "ADR-001: Match files across pipelines by tree path"
Cohesion: 0.29
Nodes (6): ADR-001: Match files across pipelines by tree path, Alternatives considered, Consequences, Context, Decision, Status

### Community 99 - "ADR-002: Comparison metrics"
Cohesion: 0.29
Nodes (6): ADR-002: Comparison metrics, Alternatives considered, Consequences, Context, Decision, Status

### Community 100 - "ADR-004: Report as JSON + self-contained HTML"
Cohesion: 0.29
Nodes (6): ADR-004: Report as JSON + self-contained HTML, Alternatives considered, Consequences, Context, Decision, Status

### Community 101 - "ADR-005: Single compare config; same schema both sides"
Cohesion: 0.29
Nodes (6): ADR-005: Single compare config; same schema both sides, Alternatives considered, Consequences, Context, Decision, Status

### Community 102 - "ADR-007: Built-in profiling"
Cohesion: 0.29
Nodes (6): ADR-007: Built-in profiling, Alternatives considered, Consequences, Context, Decision, Status

### Community 103 - "ADR-008: No ignition pack; Stage 01.5 skipped"
Cohesion: 0.29
Nodes (6): ADR-008: No ignition pack; Stage 01.5 skipped, Alternatives considered, Consequences, Context, Decision, Status

### Community 104 - "Project Rules"
Cohesion: 0.29
Nodes (6): Conventions, Domain Knowledge, Known Gotchas, Process Rules, Product Context, Project Rules

### Community 105 - "Architecture"
Cohesion: 0.29
Nodes (6): Architecture, Data flow, Forbidden dependencies, Models, Owners, Parallelism

### Community 106 - "Metrics"
Cohesion: 0.29
Nodes (6): Metrics, Per file pair, Per item, Per type family, Result keys, Text comparison algorithm

### Community 107 - "Overview"
Cohesion: 0.29
Nodes (6): Documents, End-to-end walkthrough, Overview, Problem, Success criteria, Users

### Community 109 - "JSON report structure"
Cohesion: 0.33
Nodes (6): Family entry, JSON report structure, Metric result, Other output files, run_stats, Top level

### Community 110 - "Errors and edge cases"
Cohesion: 0.33
Nodes (5): Adversarial review, Errors and edge cases, File level, Item level, Run level (fatal, exit code 1)

### Community 111 - "Outputs"
Cohesion: 0.33
Nodes (6): Examples, Files written, HTML layout, Outputs, Progress, Run statistics and profiling

### Community 113 - "Configuration reference"
Cohesion: 0.40
Nodes (5): Configuration reference, Example, Optional keys, Required keys, Type-families file

### Community 114 - ".NET port"
Cohesion: 0.40
Nodes (5): Build, test, run, CI, Intentional deviations from Python, Layout, .NET port

### Community 115 - "Scope"
Cohesion: 0.40
Nodes (4): Deferred, In scope, Out of scope, Scope

### Community 116 - "Testing strategy"
Cohesion: 0.40
Nodes (4): Example presence, Rules, Test types, Testing strategy

### Community 118 - "Configuration"
Cohesion: 0.50
Nodes (4): compare-config, Configuration, Effective config in the report, type-families

### Community 119 - "task.md"
Cohesion: 0.50
Nodes (3): Acceptance Criteria, Description, Test Strategy

### Community 120 - "PULL_REQUEST_TEMPLATE.md"
Cohesion: 0.50
Nodes (3): Summary, Test Plan, Verification Results

## Knowledge Gaps
- **231 isolated node(s):** `SnippetN`, `SendGraceSeconds`, `Clock`, `Deliver`, `BeforeDispose` (+226 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 1106 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **31 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Decision` connect `ADR-009: Port to C# .NET 8` to `ReadOnlySpan`, `ChannelHooks`, `.CompareAll`?**
  _High betweenness centrality (0.393) - this node is a cross-community bridge._
- **Are the 46 inferred relationships involving `ErrorCode` (e.g. with `compare_item()` and `_facts()`) actually correct?**
  _`ErrorCode` has 46 INFERRED edges - model-reasoned connections that need verification._
- **What connects `SnippetN`, `SendGraceSeconds`, `Clock` to the rest of the system?**
  _231 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `ExtractorTester.Models` be split into smaller, more focused modules?**
  _Cohesion score 0.05297876726448155 - nodes in this community are weakly interconnected._
- **Why does `_first_equal()` connect `ADR-009: Port to C# .NET 8` to `CompareConfig`?**
  _High betweenness centrality (0.363) - this node is a cross-community bridge._
- **Should `List` be split into smaller, more focused modules?**
  _Cohesion score 0.058419243986254296 - nodes in this community are weakly interconnected._
- **Why does `PoolOptions` connect `.CompareAll` to `ExtractorTester.Models`, `ADR-009: Port to C# .NET 8`, `ChannelHooks`, `RunnerTests`, `.CompareAll`?**
  _High betweenness centrality (0.223) - this node is a cross-community bridge._