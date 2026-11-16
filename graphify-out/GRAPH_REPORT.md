# Graph Report - extractor-tester  (2026-11-16)

## Corpus Check
- 234 files · ~168,664 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 12 file(s) not represented in the graph (top: (none) 4, .jsonl 4, .props 1)

## Summary
- 2753 nodes · 6552 edges · 127 communities (96 shown, 31 thin omitted)
- Extraction: 93% EXTRACTED · 7% INFERRED · 0% AMBIGUOUS · INFERRED: 468 edges (avg confidence: 0.9)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `a2dc506a`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- ExtractorTester.Models
- List
- runner.py
- GoldenTests
- models.py
- .Build
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
- steps
- Theory
- ydk-sync.yml
- Text
- Scanner
- PairFacts
- ydk-pr-body.yml
- output.py
- tree.py
- .Repr
- tasks
- tasks
- .CompareBuffered
- FieldDifferTests
- PyJson
- test_compare_fields.py
- corpus.py
- CompareConfig
- Interrupt
- Example
- TextFacts
- ItemComparerTests
- ydk-spec-integrity.yml
- test_golden.py
- test_pool.py
- channel.py
- Slot
- Report
- Aggregator
- extractor_tester/fields.py
- generate_fixtures.py
- test_render.py
- List
- ydk-delete-merged-branch.yml
- WorkerMessage
- compare
- pathlib
- GoldenChecks
- text_hash
- .CompareBuffered
- test_tool_error_coverage.py
- ReadOnlySpan
- bench_run
- ADR-009: Port to C# .NET 8
- ExtractorTester.Tests.csproj
- ChannelHooks
- SequenceMatcher
- cfg
- gen_aggregate_fixtures.py
- RenderTests
- GoldenCorpus
- test_generator.py
- .Serialize
- bench_run
- gen_indel_fixtures.py
- test_normalize_collapses_and_trims
- test_outputs_identical_for_one_and_four_workers
- test_normalize_unicode_whitespace
- Slowest
- Matching
- test_smoke.py
- ADR-003: Hybrid full / buffered text comparison
- ADR-006: Tech stack and scale design
- README.md
- Initial plan summary (Stage 02)
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
- Errors and edge cases
- Outputs
- Dictionary
- Configuration reference
- .NET port
- Scope
- Testing strategy
- .Stats
- Configuration
- task.md
- PULL_REQUEST_TEMPLATE.md
- Benchmark: full-text Indel similarity (rapidfuzz)
- epic.md
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
- `Decision` --references--> `Indel`  [INFERRED]
  docs/adrs/009-dotnet-port.md → dotnet/src/ExtractorTester/Indel.cs
- `Installation and quick start` --references--> `compare()`  [INFERRED]
  README.md → src/extractor_tester/cli.py
- `run_stats` --references--> `render()`  [INFERRED]
  docs/report-format.md → src/extractor_tester/render.py
- `Decision` --references--> `ChannelHooks`  [INFERRED]
  docs/adrs/009-dotnet-port.md → dotnet/src/ExtractorTester/Channel.cs

## Import Cycles
- None detected.

## Communities (127 total, 31 thin omitted)

### Community 0 - "ExtractorTester.Models"
Cohesion: 0.05
Nodes (24): ExtractorTester.Pool, ExtractorTester.Profiling, ExtractorTester.Loader, ExtractorTester.Render, ExtractorTester.Cli, ExtractorTester.Config, ExtractorTester.Aggregate, ExtractorTester.Channel (+16 more)

### Community 1 - "List"
Cohesion: 0.07
Nodes (35): IMatchTier, Name, Matcher, NameTier, Name, TextHashTier, Name, TierResult (+27 more)

### Community 2 - "runner.py"
Cohesion: 0.08
Nodes (11): RunStats, items_writer(), compare_all(), replace(), start(), _worker_main(), WorkerStats, _execute() (+3 more)

### Community 4 - "models.py"
Cohesion: 0.08
Nodes (25): diff(), _equal(), _json_type(), _snip(), _blank(), compare_item(), _facts(), _failed() (+17 more)

### Community 6 - "pytest"
Cohesion: 0.06
Nodes (26): ConfigError, load_config(), _load_families(), _read_yaml(), _slot(), test_corrupt_payload_marks_broken(), test_reader_queues_messages_then_marks_broken_on_eof(), test_bad_yaml() (+18 more)

### Community 7 - "ConfigTests"
Cohesion: 0.06
Nodes (14): ConfigException, ConfigPath, Reason, ConfigLoader, Field, Kind, Float, Int (+6 more)

### Community 8 - "test_loader.py"
Cohesion: 0.08
Nodes (21): discover(), _invalid(), load_item(), LoaderError, _scan(), cfg(), dirs(), test_empty_input_warning() (+13 more)

### Community 9 - "compare_text"
Cohesion: 0.07
Nodes (28): compare_text(), _cfg(), _edit(), _lines(), test_buffered_close_to_full_on_line_edits(), test_no_line_breaks_is_cut_and_counted(), test_one_side_without_text(), test_oversized_replaced_run_compared_block_by_block() (+20 more)

### Community 10 - "test_decode.py"
Cohesion: 0.17
Nodes (10): main(), decode_text(), _b64(), test_counter_resets_between_calls(), test_invalid_utf8_counts_sequences(), test_legit_replacement_char_not_counted(), test_malformed_base64_raises(), test_non_string_raises() (+2 more)

### Community 11 - "CompareConfig"
Cohesion: 0.05
Nodes (13): stats(), compare_buffered(), _lcs(), split_blocks(), _buffered(), _common_prefix(), _full(), _relation() (+5 more)

### Community 12 - "compare_item"
Cohesion: 0.10
Nodes (17): f(), sides(), test_exception_becomes_item_processing_failed(), test_facts_never_contain_full_text(), test_field_filled_and_emptied_hints(), test_file_level_error_attached_to_its_pair(), test_invalid_base64_skips_text_but_keeps_field_diffs(), test_item_metadata_diffs_exclude_id_and_files() (+9 more)

### Community 13 - "write_item"
Cohesion: 0.07
Nodes (27): write_item(), folders(), invoke(), item_errors(), item_files(), read_jsonl(), run_with_workers(), three_items() (+19 more)

### Community 14 - "RunnerTests"
Cohesion: 0.08
Nodes (10): CliApp, CompareRunner, PyPath, RunOptions, BeforeFinish, Interrupt, Pool, RunnerTests (+2 more)

### Community 15 - "LoaderTests"
Cohesion: 0.14
Nodes (5): ItemLoader, JsonRecursionException, LoaderException, Code, LoaderTests

### Community 16 - "build"
Cohesion: 0.12
Nodes (24): ErrorCode, build(), _find_root(), TreeError, _bad(), f(), item(), test_deep_chain() (+16 more)

### Community 17 - "gen_compare_fixtures.py"
Cohesion: 0.15
Nodes (10): field_case(), field_cases(), item_case(), item_cases(), raw_sides(), main(), raw_item(), text_case() (+2 more)

### Community 18 - "FileRecord"
Cohesion: 0.07
Nodes (14): default_tiers(), _one_sided(), _hash_key(), MatchTier, NameTier, _pair_group(), _pair_unique(), TextHashTier (+6 more)

### Community 19 - "test_output.py"
Cohesion: 0.08
Nodes (17): OutputError, pairs_writer(), prepare_out(), _mr(), _pair(), _report(), test_evil_item_id_stays_inside(), test_items_jsonl() (+9 more)

### Community 21 - "with"
Cohesion: 0.08
Nodes (31): concurrency, cancel-in-progress, group, name, runs-on, steps, jobs, dotnet (+23 more)

### Community 22 - "test_tiers.py"
Cohesion: 0.10
Nodes (15): ids(), rec(), test_empty_names_never_pair(), test_extra_same_name_files_pass_on_in_order(), test_hash_tie_break_requires_same_family(), test_names_are_case_sensitive(), test_same_name_empty_text_pairs_by_position(), test_same_name_text_hash_breaks_tie_before_position() (+7 more)

### Community 23 - "generator.py"
Cohesion: 0.29
Nodes (5): edit(), generate(), make_text(), _vocab(), _write()

### Community 24 - ".add_pair"
Cohesion: 0.08
Nodes (10): Aggregator, _Counter, _Family, Lowest, Example, FamilyAggregate, MetricResult, test_lowest_keeps_cap_lowest() (+2 more)

### Community 25 - "test_aggregate.py"
Cohesion: 0.16
Nodes (15): _cfg(), _item(), _pair(), _run(), test_cut_line_warning_and_hints(), test_decode_errors_summed(), test_deterministic(), test_errors_by_code() (+7 more)

### Community 26 - "test_matcher.py"
Cohesion: 0.17
Nodes (11): match_item(), item(), rec(), summary(), test_children_pair_and_leftovers_have_no_candidate(), test_custom_tier_list_labels_pairs(), test_descendants_of_unpaired_parent_are_parent_unpaired(), test_paired_children_are_walked_in_turn() (+3 more)

### Community 27 - "ErrorCode"
Cohesion: 0.09
Nodes (21): ErrorCode, DUPLICATE_FILE_ID, EMPTY_INPUT, INPUT_NOT_FOUND, INTERNAL_FAILURE, INVALID_BASE64, INVALID_CONFIG, INVALID_JSON (+13 more)

### Community 29 - "steps"
Cohesion: 0.08
Nodes (28): if, name, runs-on, steps, if, name, runs-on, steps (+20 more)

### Community 30 - "steps"
Cohesion: 0.07
Nodes (29): concurrency, cancel-in-progress, group, env, DEFAULT_BRANCH, GH_TOKEN, GRAPHIFY_BRANCH, if (+21 more)

### Community 32 - "ydk-sync.yml"
Cohesion: 0.08
Nodes (24): concurrency, cancel-in-progress, group, env, GH_REPO, GH_TOKEN, YDK_VERSION, jobs (+16 more)

### Community 33 - "Text"
Cohesion: 0.17
Nodes (3): InvalidBase64Exception, Text, DecodeParityTests

### Community 34 - "Scanner"
Cohesion: 0.07
Nodes (12): JsonIntDigitsException, PyDecodeException, PyJsonDecoder, Scanner, StopIteration, Idx, JsonlWriter, Path (+4 more)

### Community 35 - "PairFacts"
Cohesion: 0.06
Nodes (32): FieldDiff, Group, NewType, NewValue, OldType, OldValue, Path, Status (+24 more)

### Community 36 - "ydk-pr-body.yml"
Cohesion: 0.08
Nodes (24): concurrency, cancel-in-progress, group, env, BASE_REF, PR_BODY, YDK_VERSION, jobs (+16 more)

### Community 37 - "output.py"
Cohesion: 0.11
Nodes (4): Report, JsonlWriter, _write_failed(), write_report()

### Community 38 - "tree.py"
Cohesion: 0.07
Nodes (17): InvalidBase64Error, family(), mime(), flatten(), add(), clock(), _check_unreachable(), _esc() (+9 more)

### Community 39 - ".Repr"
Cohesion: 0.12
Nodes (3): AutoEscapeContext, HtmlRenderer, PyText

### Community 40 - "tasks"
Cohesion: 0.09
Nodes (22): epics, description, id, spec_refs, title, stories, acceptance, component_refs (+14 more)

### Community 41 - "tasks"
Cohesion: 0.09
Nodes (22): epics, description, id, spec_refs, title, stories, acceptance, component_refs (+14 more)

### Community 45 - "test_compare_fields.py"
Cohesion: 0.12
Nodes (11): is_empty(), by_path(), cfg(), test_ignored_and_structural_skipped_sorted_groups(), test_is_empty(), test_lists_whole_and_nested_numbers(), test_nan_same(), test_null_is_value() (+3 more)

### Community 46 - "corpus.py"
Cohesion: 0.16
Nodes (9): build(), export_dotnet(), _item(), normalize(), normalized_outputs(), regenerate(), _root(), run() (+1 more)

### Community 47 - "CompareConfig"
Cohesion: 0.09
Nodes (21): CompareConfig, ContainmentPct, CutLinesWarnPct, ExamplesPerResult, FullCompareMaxChars, GenericFields, IgnoreFields, ItemTimeoutSeconds (+13 more)

### Community 48 - "Interrupt"
Cohesion: 0.22
Nodes (4): Interrupt, ProcessInformation, SecurityAttributes, StartupInfo

### Community 49 - "Example"
Cohesion: 0.25
Nodes (9): Lowest, Example, ItemId, NewValue, Note, OldValue, PathNew, PathOld (+1 more)

### Community 50 - "TextFacts"
Cohesion: 0.10
Nodes (20): TextFacts, EditSimilarity, Jaccard, LengthDelta, LinesCut, LinesCutPct, LinesTotal, Method (+12 more)

### Community 52 - "ydk-spec-integrity.yml"
Cohesion: 0.10
Nodes (19): name, runs-on, steps, concurrency, cancel-in-progress, group, env, YDK_VERSION (+11 more)

### Community 53 - "test_golden.py"
Cohesion: 0.06
Nodes (26): field_names(), _key_forms(), _key_patterns(), _lists_named(), metric_results(), missing_examples(), _placeholder_patterns(), tool_error_codes() (+18 more)

### Community 54 - "test_pool.py"
Cohesion: 0.20
Nodes (6): _compare_all(), test_result_already_received_beats_timeout(), test_stalled_sender_does_not_block_other_workers(), test_worker_hung_at_startup_is_replaced(), test_worker_killed_mid_write_marks_only_its_item(), test_workers_dying_before_taking_items_end_the_run()

### Community 55 - "channel.py"
Cohesion: 0.18
Nodes (5): deliver(), open_slot(), _reader(), recv(), Slot

### Community 56 - "Slot"
Cohesion: 0.11
Nodes (14): Slot, Broken, BusySince, Conn, DeadSince, Inbox, Msgs, Proc (+6 more)

### Community 57 - "Report"
Cohesion: 0.04
Nodes (47): FamilyAggregate, CutLinesWarning, DecodeErrors, EditSimilarity, Family, FamilyChanged, Fields, FileErrors (+39 more)

### Community 58 - "Aggregator"
Cohesion: 0.18
Nodes (5): Aggregator, SnippetN, ExampleCounter, FamilyState, AggregateTests

### Community 59 - "extractor_tester/fields.py"
Cohesion: 0.13
Nodes (8): classify(), _covers(), cfg(), test_classify(), test_classify_ignore_beats_generic(), test_flatten_basic(), test_flatten_collision_raises(), test_flatten_deep()

### Community 60 - "generate_fixtures.py"
Cohesion: 0.26
Nodes (6): _file(), golden(), main(), _replace(), samples(), _write()

### Community 61 - "test_render.py"
Cohesion: 0.09
Nodes (16): Family entry, JSON report structure, Metric result, Other output files, run_stats, Top level, render(), test_matching_does_not_import_compare_or_render() (+8 more)

### Community 62 - "List"
Cohesion: 0.07
Nodes (33): ItemComparer, DiscoveredItem, Discovery, ErrorRecord, Code, FilePath, ItemId, Message (+25 more)

### Community 63 - "ydk-delete-merged-branch.yml"
Cohesion: 0.12
Nodes (16): if, runs-on, steps, BRANCH, GH_TOKEN, jobs, delete-branch, name (+8 more)

### Community 64 - "WorkerMessage"
Cohesion: 0.19
Nodes (3): WorkerChannel, WorkerMessage, ChannelTests

### Community 65 - "compare"
Cohesion: 0.13
Nodes (7): Documentation, extractor-tester, Input format, Installation and quick start, Output, Process, compare()

### Community 66 - "pathlib"
Cohesion: 0.09
Nodes (8): _read_stdin(), main(), mutate(), cases(), main(), match_case(), tier_case(), cfg()

### Community 68 - "text_hash"
Cohesion: 0.17
Nodes (6): text_hash(), test_text_hash_empty_is_none(), test_text_hash_is_sha256(), test_text_hash_limit_truncates(), test_text_hash_nonpositive_limit_is_none(), test_text_hash_whitespace_differences_equal()

### Community 71 - "test_tool_error_coverage.py"
Cohesion: 0.22
Nodes (3): _Collector, marked_codes(), test_every_error_code_has_a_marked_test()

### Community 73 - "bench_run"
Cohesion: 0.18
Nodes (5): BenchRun, test_full_compare_pair(), test_html_size(), test_peak_memory(), test_run_time_100k()

### Community 74 - "ADR-009: Port to C# .NET 8"
Cohesion: 0.33
Nodes (5): ADR-009: Port to C# .NET 8, Alternatives considered, Consequences, Context, Status

### Community 75 - "ExtractorTester.Tests.csproj"
Cohesion: 0.15
Nodes (9): Microsoft.NET.Sdk, Microsoft.NET.Sdk, coverlet.collector (6.0.0), Microsoft.NET.Test.Sdk (17.8.0), Scriban (7.5.3), System.CommandLine (2.0.0), xunit (2.5.3), xunit.runner.visualstudio (2.5.3) (+1 more)

### Community 76 - "ChannelHooks"
Cohesion: 0.13
Nodes (12): Decision, ChannelHooks, BeforeDispose, Clock, Deliver, ReaderJoinMilliseconds, SendGraceSeconds, PoolOptions (+4 more)

### Community 77 - "SequenceMatcher"
Cohesion: 0.35
Nodes (4): MatchingBlock, Opcode, SequenceMatcher, SequenceMatcherTests

### Community 79 - "gen_aggregate_fixtures.py"
Cohesion: 0.43
Nodes (4): build_scenarios(), build_util(), main(), scenario()

### Community 82 - "test_generator.py"
Cohesion: 0.33
Nodes (3): _snapshot(), test_median_length_within_target(), test_same_seed_same_corpus()

### Community 84 - "bench_run"
Cohesion: 0.29
Nodes (3): bench_run(), sample(), _tree_rss()

### Community 85 - "gen_indel_fixtures.py"
Cohesion: 0.60
Nodes (3): main(), mutate(), rand_str()

### Community 87 - "test_outputs_identical_for_one_and_four_workers"
Cohesion: 0.33
Nodes (3): _jsonl_untimed(), test_outputs_identical_for_one_and_four_workers(), _untimed()

### Community 89 - "Slowest"
Cohesion: 0.12
Nodes (5): Slowest, take(), test_aggregator_sums_item_stages_with_every_stage_present(), test_decode_accumulates_and_resets(), test_slowest_keeps_top_n_descending()

### Community 90 - "Matching"
Cohesion: 0.22
Nodes (8): Matching, The tree, The walk, Tier list as the single source of matching rules, Tiers, Type family, Why matching is needed, Worked example

### Community 92 - "test_smoke.py"
Cohesion: 0.11
Nodes (7): main(), setup(), test_missing_input_exits_1(), test_unknown_option_exits_2(), test_compare_help_lists_arguments(), test_module_imports(), test_write_item_round_trips_text()

### Community 93 - "ADR-003: Hybrid full / buffered text comparison"
Cohesion: 0.25
Nodes (7): ADR-003: Hybrid full / buffered text comparison, Alternatives considered, Amendment (2026-10-26, spec writing), Consequences, Context, Decision, Status

### Community 94 - "ADR-006: Tech stack and scale design"
Cohesion: 0.25
Nodes (7): ADR-006: Tech stack and scale design, Alternatives considered, Amendment (2026-10-26): per-item timeout, Consequences, Context, Decision, Status

### Community 96 - "Initial plan summary (Stage 02)"
Cohesion: 0.25
Nodes (7): Checks, Config, Demo increments, Epics, First ready task, Initial plan summary (Stage 02), Schedule (`ydk task plan-waves --agents 3`)

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

### Community 110 - "Errors and edge cases"
Cohesion: 0.33
Nodes (5): Adversarial review, Errors and edge cases, File level, Item level, Run level (fatal, exit code 1)

### Community 111 - "Outputs"
Cohesion: 0.33
Nodes (6): Examples, Files written, HTML layout, Outputs, Progress, Run statistics and profiling

### Community 112 - "Dictionary"
Cohesion: 0.16
Nodes (6): Stages, Slowest, TreeBuilder, TreeException, Code, ProfilingTests

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
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 1128 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **31 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Decision` connect `ChannelHooks` to `ReadOnlySpan`, `ADR-009: Port to C# .NET 8`?**
  _High betweenness centrality (0.392) - this node is a cross-community bridge._
- **Are the 46 inferred relationships involving `ErrorCode` (e.g. with `compare_item()` and `_facts()`) actually correct?**
  _`ErrorCode` has 46 INFERRED edges - model-reasoned connections that need verification._
- **What connects `SnippetN`, `SendGraceSeconds`, `Clock` to the rest of the system?**
  _231 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `ExtractorTester.Models` be split into smaller, more focused modules?**
  _Cohesion score 0.05191919191919192 - nodes in this community are weakly interconnected._
- **Why does `_first_equal()` connect `ChannelHooks` to `CompareConfig`?**
  _High betweenness centrality (0.354) - this node is a cross-community bridge._
- **Should `List` be split into smaller, more focused modules?**
  _Cohesion score 0.06930379746835443 - nodes in this community are weakly interconnected._
- **Why does `PoolOptions` connect `ChannelHooks` to `ExtractorTester.Models`, `RunnerTests`, `Dictionary`, `.CompareAll`, `List`?**
  _High betweenness centrality (0.209) - this node is a cross-community bridge._