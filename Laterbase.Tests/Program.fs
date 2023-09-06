open FsCheck
open Laterbase.Core
open Laterbase.Simulated

System.Console.Clear ()

let revRevIsOrig (xs:list<int>) = List.rev (List.rev xs) = xs 
Check.Quick revRevIsOrig
