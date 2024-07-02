# NP.NetBinaryAdjuster
this project is an offshoot of [IgnoresAccessChecksToGenerator](https://github.com/aelij/IgnoresAccessChecksToGenerator) by Eli Arbel and others. Most of the code comes from the them. 

I needed this capability in order to modify Avalonia DataGrid - to allow more flexibility in building the group header rows and some other functionality without modifying the whole DataGrid code.

I added capability to make all non-public methods (including private and protected, but not virtual or overridden) public for some types.
Also I added capability to make some (e.g. private) methods - virtual protected in order to enable the overrides in subclasses. 

Also I renamed the executable and the project to imply more generic application rather than simply turning internal methods into public. 
