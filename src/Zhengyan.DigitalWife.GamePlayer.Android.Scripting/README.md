# Android scripting contracts

This portable `net10.0` assembly contains the script globals, entity/scene APIs,
platform service interfaces, and publication compiler used by both GameEditor
and the Android player. The editor can compile scripts without loading Android
framework assemblies. Platform implementations remain in GamePlayer.Android.

`RuntimeLlm`, `RuntimeLlmSkillTools`, and `MainThreadDispatcher` are source-linked
from GamePlayer. `ANDROID_SCRIPT_RUNTIME` binds their entity/scene parameters to
the Android script wrappers, so both platforms run the same tool-call protocol.

Reference this assembly with the `AndroidScripting` alias from desktop tools.
Its linked LLM types share the desktop namespace; desktop script compilers must
exclude Android assemblies, and Android compilation must reference its own
dependency graph. The regression suite checks both compilers in one process.

Native callback producers enqueue events on `AndroidCSharpScriptHost`'s dispatcher.
LLM events are already dispatched on that thread and must not be queued a second
time, which would reorder tool notifications and execution.
