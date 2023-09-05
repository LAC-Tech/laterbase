open FsCheck
open Library

System.Console.Clear ()

let revRevIsOrig (xs:list<int>) = List.rev (List.rev xs) = xs 

Check.Quick revRevIsOrig
