Imports OP2UtilityDotNet.OP2Map

Public Class fCellTypes

    ' Reference to the selected cell type
    Public SelectedCellType As CellType = CellType.FastPassible1

    Private cellTypeMap As Dictionary(Of Integer, CellType)
    Private Sub fCellTypes_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        ' Set form properties
        Me.FormBorderStyle = FormBorderStyle.FixedToolWindow
        Me.Text = "Cell Types"
        Me.StartPosition = FormStartPosition.Manual
        Me.Location = New Point(Screen.PrimaryScreen.WorkingArea.Width - Me.Width - 20, 100)
        Me.TopMost = True

        ' Initialize the cell type mapping dictionary
        InitializeCellTypeMap()

        ' Populate cell types list
        PopulateCellTypesList()
    End Sub

    Private Sub InitializeCellTypeMap()
        cellTypeMap = New Dictionary(Of Integer, CellType)
        cellTypeMap.Add(0, CellType.FastPassible1)
        cellTypeMap.Add(1, CellType.FastPassible2)
        cellTypeMap.Add(2, CellType.MediumPassible1)
        cellTypeMap.Add(3, CellType.MediumPassible2)
        cellTypeMap.Add(4, CellType.SlowPassible1)
        cellTypeMap.Add(5, CellType.SlowPassible2)
        cellTypeMap.Add(6, CellType.Impassible1)
        cellTypeMap.Add(7, CellType.Impassible2)
        cellTypeMap.Add(8, CellType.NorthCliffs)
        cellTypeMap.Add(9, CellType.CliffsHighSide)
        cellTypeMap.Add(10, CellType.CliffsLowSide)
        cellTypeMap.Add(11, CellType.VentsAndFumaroles)
        cellTypeMap.Add(12, CellType.zPad12)
        cellTypeMap.Add(13, CellType.zPad13)
        cellTypeMap.Add(14, CellType.zPad14)
        cellTypeMap.Add(15, CellType.zPad15)
        cellTypeMap.Add(16, CellType.zPad16)
        cellTypeMap.Add(17, CellType.zPad17)
        cellTypeMap.Add(18, CellType.zPad18)
        cellTypeMap.Add(19, CellType.zPad19)
        cellTypeMap.Add(20, CellType.zPad20)
        cellTypeMap.Add(21, CellType.DozedArea)
        cellTypeMap.Add(22, CellType.Rubble)
        cellTypeMap.Add(23, CellType.NormalWall)
        cellTypeMap.Add(24, CellType.MicrobeWall)
        cellTypeMap.Add(25, CellType.LavaWall)
        cellTypeMap.Add(26, CellType.Tube0)
        cellTypeMap.Add(27, CellType.Tube1)
        cellTypeMap.Add(28, CellType.Tube2)
        cellTypeMap.Add(29, CellType.Tube3)
        cellTypeMap.Add(30, CellType.Tube4)
        cellTypeMap.Add(31, CellType.Tube5)
    End Sub


    Private Sub PopulateCellTypesList()
        ' Clear existing items
        cmbCellTypeList.Items.Clear()

        ' Add all cell types with descriptive text
        cmbCellTypeList.Items.Add("Fast Passable 1 (F1)")
        cmbCellTypeList.Items.Add("Fast Passable 2 (F2)")
        cmbCellTypeList.Items.Add("Medium Passable 1 (M1)")
        cmbCellTypeList.Items.Add("Medium Passable 2 (M2)")
        cmbCellTypeList.Items.Add("Slow Passable 1 (S1)")
        cmbCellTypeList.Items.Add("Slow Passable 2 (S2)")
        cmbCellTypeList.Items.Add("Impassable 1 (I1)")
        cmbCellTypeList.Items.Add("Impassable 2 (I2)")
        cmbCellTypeList.Items.Add("North Cliffs (NC)")
        cmbCellTypeList.Items.Add("Cliffs - High Side (CHS)")
        cmbCellTypeList.Items.Add("Cliffs - Low Side (CLS)")
        cmbCellTypeList.Items.Add("Vents and Fumaroles (V)")
        cmbCellTypeList.Items.Add("zPad12 (Z12)")
        cmbCellTypeList.Items.Add("zPad13 (Z13)")
        cmbCellTypeList.Items.Add("zPad14 (Z14)")
        cmbCellTypeList.Items.Add("zPad15 (Z15)")
        cmbCellTypeList.Items.Add("zPad16 (Z16)")
        cmbCellTypeList.Items.Add("zPad17 (Z17)")
        cmbCellTypeList.Items.Add("zPad18 (Z18)")
        cmbCellTypeList.Items.Add("zPad19 (Z19)")
        cmbCellTypeList.Items.Add("zPad20 (Z20)")
        cmbCellTypeList.Items.Add("Bulldozed (D)")
        cmbCellTypeList.Items.Add("Rubble (R)")
        cmbCellTypeList.Items.Add("Normal Wall (NW)")
        cmbCellTypeList.Items.Add("Microbe Wall (MW)")
        cmbCellTypeList.Items.Add("Lava Wall (LW)")
        cmbCellTypeList.Items.Add("Tube 0 (T0)")
        cmbCellTypeList.Items.Add("Tube 1 (T1)")
        cmbCellTypeList.Items.Add("Tube 2 (T2)")
        cmbCellTypeList.Items.Add("Tube 3 (T3)")
        cmbCellTypeList.Items.Add("Tube 4 (T4)")
        cmbCellTypeList.Items.Add("Tube 5 (T5)")

        ' Select the first item by default
        If cmbCellTypeList.Items.Count > 0 Then
            cmbCellTypeList.SelectedIndex = 0
        End If
    End Sub

    Private Sub cmbCellTypeList_SelectedIndexChanged(sender As Object, e As EventArgs) Handles cmbCellTypeList.SelectedIndexChanged
        ' Use the dictionary to look up the cell type
        Dim index As Integer = cmbCellTypeList.SelectedIndex

        If cellTypeMap.ContainsKey(index) Then
            SelectedCellType = cellTypeMap(index)
            GlobalVariables.ActiveCellType = SelectedCellType
            Debug.WriteLine("Set ActiveCellType to " & SelectedCellType.ToString)
        Else
            Debug.WriteLine("Error: Invalid index " & index)
        End If
    End Sub

End Class