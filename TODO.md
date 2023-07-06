### TODO

- Investigate IGenericParameterProvider in ImportReference (I think I've been overcomplicating everything)
- EventDefinition?
- Exception handlers?
- Fix documentation
- Seal everything
- Do something if type inject already exists in target assembly
- More user-friendly error handling.
- Add blank constructors to attributes that use the name of the method / field as their parameter.
- Extension methods to access mixin methods?
- Inject interface implimentations
- Injectable attributes
- Work out in-assembly resources
- Unit tests!
- Debug symbol preservation

### DONE

- Make it so other types in the mods assemblies can be accessed at runtime
- Check if generic constraints on types and methods match
- Other special method injection (operators, destructors, etc.)
- Other method and constructor mixin location (Head, tail and return?)
- Add method accessors to mixins
- Mixin priority
- Constructor injection
- Static constructor injection
