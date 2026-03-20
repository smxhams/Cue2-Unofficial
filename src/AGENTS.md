# Cue2 Agent Guidelines

## Project Overview
This project is a multi-platform event playback software, similar to QLab 5, developed in C# using Godot 4.6.1 Mono, FFmpeg.AutoGen 8.0.0.1, and SDL3-CS 3.4.2 The software targets Windows 10+, macOS, and Linux, with a focus on minimal OS-dependent code to ensure cross-platform compatibility. The project will be open-sourced, so code must be clean, modular, well-documented, and maintainable for community contributions.

This project is public on GitHub at: https://github.com/smxhams/Cue2-Unofficial. Refer to the GitHub page to fill in any knowledge gaps.

## Software Features & Requirements
- **Audio playback**: Support as many formats as practical
- **Video playback**: Support as many formats as practical
- **OSC commands**: Send and receive
- **Text overlay**: Display text overlays
- **Session management**: Save and load sessions
- **Undo/Redo**: Capability to undo/redo changes to cues
- **Cue library**: Store and load cues from a library

**Performance Requirements:**
- Minimal latency when triggering cues
- Pre-loading options for upcoming/selected cues to improve trigger speed
- Optimization for wide hardware compatibility while prioritizing no stuttering or delays in playback

## Build Commands
- .csproj for building is here: 
  - Windows: `C:\MyFiles\Cue2_Home\Cue2\Cue2.csproj`
  - macOS: `/Users/smxham/Library/CloudStorage/GoogleDrive-smxham@gmail.com/Other computers/My Computer/Cue2/Cue2.csproj`
- **Build**: `dotnet build <path-to-csproj>` (Godot.NET.Sdk handles compilation)
- **Run**: Use Godot editor or `godot --path . --run` for runtime execution
- **Clean**: `dotnet clean <path-to-csproj>`

## Test Commands
- No dedicated test framework - use Godot editor for UI testing
- Manual testing through application interface
- Write unit tests for critical components using NUnit where possible

## Code Style Guidelines

### Naming Conventions
- **Classes**: PascalCase (e.g., `GlobalData`, `CueCommandInterpreter`)
- **Properties/Methods**: PascalCase (e.g., `PreWait`, `GetNode()`)
- **Private fields**: camelCase with underscore prefix (e.g., `_globalSignals`)
- **Namespaces**: Follow folder structure (e.g., `Cue2.Base.Classes.CueTypes`)
- Use clear, descriptive, and consistent naming; avoid abbreviations unless widely understood

### Imports & Structure
- System imports first, then Godot, then project-specific
- Use `using` statements at top of file
- Prefer explicit namespaces over `using` for Godot collections
- Structure code into reusable, loosely coupled components with clear interfaces
- Prefer dependency injection where applicable to enhance testability

### Types & Error Handling
- Use strong typing with `int`, `double`, `string`, etc.
- Properties with getters/setters for public access
- Enums for constrained values (e.g., `FollowType`)
- XML documentation comments for all public classes, methods, and properties
- Implement comprehensive error handling using try-catch blocks and custom exceptions
- Log errors via `globalSignals.Log` for user feedback
- Ensure errors are meaningful and actionable

### Performance & Thread Safety
- Optimize for performance, especially for media playback and event scheduling
- Leverage asynchronous programming (async/await) where appropriate
- Ensure thread-safe operations, particularly when interfacing with FFmpeg or SDL3-CS
- Minimize resource usage and memory allocations

### Testing & Documentation
- Design code with testability in mind (separate logic from Godot nodes)
- Include inline comments for complex logic or non-obvious decisions
- Follow Git best practices with clear commit messages and logical branch naming

### Formatting
- 4-space indentation (Godot default)
- One class per file matching filename
- Group related properties/methods together

## XML Documentation Standards

### Required XML Comments
All public classes, methods, properties, and events must include XML documentation comments using triple-slash (///) syntax for auto-generated documentation.

### Class Documentation Format
```csharp
/// <summary>
/// Brief description of the class's purpose and responsibilities.
/// </summary>
/// <remarks>
/// Optional detailed explanation, usage examples, or important notes.
/// </remarks>
public class ClassName
```

### Method Documentation Format
```csharp
/// <summary>
/// Brief description of what the method does.
/// </summary>
/// <param name="parameterName">Description of the parameter's purpose and expected values.</param>
/// <returns>Description of the return value, or void if none.</returns>
/// <exception cref="ExceptionType">Description of when this exception is thrown.</exception>
public ReturnType MethodName(ParameterType parameterName)
```

### Property Documentation Format
```csharp
/// <summary>
/// Brief description of the property's purpose.
/// </summary>
/// <value>Description of the property value and its valid range/format.</value>
public PropertyType PropertyName { get; set; }
```

### Event Documentation Format
```csharp
/// <summary>
/// Brief description of when the event is raised.
/// </summary>
/// <remarks>
/// Optional details about event arguments or usage patterns.
/// </remarks>
public event EventHandler<EventArgs> EventName;
```

### Documentation Generation
- Use `dotnet doc` or DocFX to generate HTML documentation from XML comments
- Include generated documentation in releases for developer reference
- Ensure all public APIs are fully documented before releases

## Cross-Platform Considerations
- Minimize platform-specific code using Godot's built-in APIs
- Handle platform differences via conditional compilation (#if directives) only when unavoidable
- Test thoroughly on all target platforms (Windows 10+, macOS, Linux) for consistent behavior

## Error Reporting and Logging
- Use `globalSignals.Log` for user-facing error messages and debugging logs
- Categorize logs (Info, Warning, Error) with timestamps and context
- Sanitize sensitive information before logging
- All `GD.Print` statements must be prefaced by script and function name (e.g., `GD.Print("ScriptName:FunctionName - message")`)

## Open-Source Readiness
- Include clear project structure with README.md detailing setup, dependencies, and contribution guidelines
- Avoid hard-coded values; use configuration files or environment variables
- Follow SOLID principles, DRY, and KISS for maintainable code

## Open Source Contribution Guidelines

### Pull Request Process
- Create feature branches from `main` with descriptive names (e.g., `feature/audio-playback-improvements`)
- Ensure all tests pass and code follows style guidelines
- Include XML documentation for new public APIs
- Update CHANGELOG.md for user-facing changes
- Request review from maintainers before merging

### Issue Reporting
- Use issue templates for bug reports and feature requests
- Include reproduction steps, expected vs actual behavior, and environment details
- Label issues appropriately (bug, enhancement, documentation, etc.)

### Code of Conduct
- Follow contributor covenant or similar code of conduct
- Be respectful and constructive in all interactions
- Focus on collaborative improvement rather than criticism

### Release Process
- Use semantic versioning (MAJOR.MINOR.PATCH)
- Create GitHub releases with release notes
- Include compiled binaries and documentation
- Tag releases in Git repository

## Architecture Patterns

### MVVM-Like Pattern
- **UI Layer**: Godot scenes and controls (Views)
- **Business Logic**: Classes in `Base/Classes/` (ViewModels/Controllers)
- **Data Layer**: `GlobalData` autoload for shared state
- **Communication**: `GlobalSignals` for decoupled event handling

### Key Integration Points
- **GlobalData**: Central state management and service locator
- **GlobalSignals**: Event-driven communication between components
- **SceneLoader**: Dynamic scene loading and instantiation
- **SaveManager**: Serialization and persistence layer

### Common Patterns to Follow
- **Factory Pattern**: For creating cue components and devices
- **Observer Pattern**: Via Godot signals for UI updates
- **Strategy Pattern**: For different playback implementations
- **Command Pattern**: For undo/redo functionality

## Common Pitfalls to Avoid

### Performance Anti-patterns
- **Blocking UI Thread**: Never perform long operations on main thread
- **Memory Leaks**: Always dispose FFmpeg contexts and SDL resources
- **Excessive Allocations**: Reuse buffers and objects where possible
- **Synchronous I/O**: Use async operations for file access

### Threading Issues
- **Race Conditions**: Use proper synchronization when accessing shared state
- **Deadlocks**: Avoid nested locks and long-held locks
- **UI Updates from Background Threads**: Use `CallDeferred()` or signals

### Godot-Specific Issues
- **Node References**: Check `IsInstanceValid()` before accessing nodes
- **Scene Loading**: Use `SceneLoader` utility for consistent loading
- **Signal Connections**: Always disconnect signals in `_ExitTree()`

## Debugging Strategies

### Audio/Video Issues
- Check FFmpeg error codes and log them with `GetFFmpegError()`
- Verify SDL audio device initialization and buffer sizes
- Use `GD.Print()` with timestamps for timing analysis
- Monitor memory usage during playback sessions

### UI Synchronization Issues
- Use `GlobalSignals` for cross-component communication
- Implement proper state validation in inspectors
- Check signal connections are established correctly
- Verify UI updates happen on main thread

### Performance Debugging
- Profile with Godot's built-in profiler
- Monitor thread contention with logging
- Check for excessive allocations with memory profiler
- Validate timing-sensitive operations with high-precision timers

## Testing Strategies

### Unit Testing Approach
- Test business logic separately from Godot nodes
- Mock external dependencies (FFmpeg, SDL, file system)
- Focus on edge cases in cue timing and state transitions
- Test error conditions and recovery scenarios

### Integration Testing
- Test complete cue playback workflows
- Verify UI state synchronization
- Test session save/load cycles
- Validate cross-platform compatibility

### Manual Testing Checklist
- Audio playback across different formats
- Video playback with overlays
- OSC command sending/receiving
- Session persistence and restoration
- Undo/redo functionality
- Multi-cue timing and synchronization

## Communication Preferences

### When to Provide Context
- **Always include**: Specific file paths, class names, method names
- **Include when relevant**: Error messages, stack traces, reproduction steps
- **Specify**: Expected vs actual behavior, performance requirements

### When I Should Ask Questions
- **Unclear requirements**: When multiple interpretations are possible
- **Missing context**: When architectural decisions impact the solution
- **Performance trade-offs**: When optimization choices need business input
- **Breaking changes**: When modifications affect existing functionality

### Preferred Interaction Style
- **Direct and concise**: Focus on actionable information
- **Proactive suggestions**: Offer alternatives and improvements
- **Reality checks**: Constructively challenge unrealistic expectations
- **Step-by-step guidance**: Break down complex tasks into manageable pieces

## Workflow Optimization

### Task Planning
- **Break down complex tasks**: Split into smaller, verifiable steps
- **Prioritize by impact**: Address critical path items first
- **Consider dependencies**: Plan around integration points
- **Include verification**: Build testing into each step

### Code Review Preparation
- **Self-review first**: Check against guidelines before submission
- **Test thoroughly**: Verify functionality across use cases
- **Document decisions**: Explain non-obvious implementation choices
- **Consider edge cases**: Handle error conditions and boundary values

## Response Expectations for AI Assistance
- Provide C# code snippets compatible with Godot 4.6 Mono, FFmpeg.AutoGen 7.1.1, and SDL3-CS 3.3.7
- Include error handling and logging in all code examples
- If a user's idea is flawed/unrealistic, provide constructive reality checks with alternatives
- Suggest optimizations for performance, readability, or maintainability
- Break down complex implementations into steps with explanations
- For code changes, add `//!!!` at the end of modified lines
- Act as a smart, experienced mentor who prioritizes truth over flattery

## Framework Preferences
- Use Godot's C# API over raw GDScript when possible
- Prefer async/await patterns for I/O operations
- Use Godot's signal system over direct method calls for UI updates
- Follow the existing MVVM-like pattern with separation between UI, business logic, and data
- Use dependency injection through GlobalData/GlobalSignals autoload pattern
- Keep FFmpeg operations isolated in dedicated decoder classes

## Code Review Checklist
- Ensure proper resource disposal (IDisposable, Dispose() calls)
- Check for thread safety in audio processing code
- Verify FFmpeg context lifecycle management
- Confirm signal connections are properly cleaned up
- Validate cross-platform compatibility
- Verify XML documentation completeness for public APIs
- Check for proper error handling and logging
- Ensure code follows SOLID principles and DRY