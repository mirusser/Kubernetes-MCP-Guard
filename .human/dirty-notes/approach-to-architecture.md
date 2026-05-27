While designing architecture in this new paradigm with agents in the picture, I find that I can’t just slap “clean architecture”, “monolith”, “modular monolith”, “vertical slices”, “microservices”, or whatever on it and call it a day.

I find that I have to consider completely new realm of possibilites with them AI agents:

- the caller may be probabilistic;
- the caller may be prompt-injected;
- tool descriptions become part of the control plane;
- a “request” may be generated, not intentionally authored;
- approval text can be spoofed;
- context can be poisoned;
- output can become input to another action;
- reads can leak secrets;
- writes can mutate real infrastructure;
- etc, etc;

So, more and more, I feel the approach is:
design from trust boundaries, not from folder-pattern religion.