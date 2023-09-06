open FsCheck
open Laterbase.Core

System.Console.Clear ()

let revRevIsOrig (xs:list<int>) = List.rev (List.rev xs) = xs 

Check.Quick revRevIsOrig
