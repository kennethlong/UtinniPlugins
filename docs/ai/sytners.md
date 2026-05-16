# SytnersUtinniPlugin

A placeholder plugin tree. As of writing it contains a single header file:

`SytnersUtinniPlugin/sup.h`:

```cpp
/*
   ███████╗██╗   ██╗████████╗███╗   ██╗███████╗██████╗
   ██╔════╝╚██╗ ██╔╝╚══██╔══╝████╗  ██║██╔════╝██╔══██╗
   ███████╗ ╚████╔╝    ██║   ██╔██╗ ██║█████╗  ██████╔╝
   ╚════██║  ╚██╔╝     ██║   ██║╚██╗██║██╔══╝  ██╔══██╗
   ███████║   ██║      ██║   ██║ ╚████║███████╗██║  ██║
   ╚══════╝   ╚═╝      ╚═╝   ╚═╝  ╚═══╝╚══════╝╚═╝  ╚═╝
*/

#pragma once

using utinni_plugin = int;   // (placeholder)
```

There is no implementation, no project file, no plugin contract being
fulfilled. The tree is reserved for future plugin work by James Webb
(Sytner), credited in the Utinni README as the maintainer who pushed for
the initial release.

## What this means for us

- **Not a working plugin.** Loading the `SytnersUtinniPlugin/` directory
  via `ut.ini` would do nothing because the directory has no DLL.
- **Worth keeping in the fork** because (a) it's where Sytner's plugin will
  eventually land upstream and (b) the empty tree carries no maintenance
  cost.
- **Don't take dependencies on it.** Treat it as if the directory weren't
  there.

If at some point a real plugin lands here, we'll fold it into [the Jawa
Toolbox docs](jawa-toolbox.md) style or write a dedicated page.
