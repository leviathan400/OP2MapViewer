﻿Imports OP2UtilityDotNet.OP2Map

' OP2MapViewer

Module GlobalVariables

    Public ApplicationName As String = "OP2MapViewer"
    Public Version As String = "0.3.0.0"
    Public Build As String = "0051"


    'Which cell type do we have selected and want to place?
    'Active as in actively selected tool/item
    Public Property ActiveCellType As CellType = CellType.FastPassible1

End Module
