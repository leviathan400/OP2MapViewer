﻿Imports System.IO
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Reflection
Imports System.Diagnostics
Imports System.Runtime.CompilerServices
Imports OP2UtilityDotNet
Imports OP2UtilityDotNet.Bitmap
Imports OP2UtilityDotNet.OP2Map
Imports OP2UtilityDotNet.Sprite
Imports OP2MapJsonTools

' OP2MapViewer
' https://github.com/leviathan400/OP2MapViewer
'
' Outpost 2 map viewer. Application to load and view .map files; the tile data and cell types.
'
'
' Outpost 2: Divided Destiny is a real-time strategy video game released in 1997.

Public Class fMain

#Region "Fields and Properties"
    ' Map-related fields
    Private currentMapFile As String
    Private currentMapPath As String
    Private currentMap As Map

    ' Resource management
    Private resourceManager As ResourceManager
    Private mapBitmap As Bitmap
    Private tilesetImages As New Dictionary(Of String, List(Of Bitmap))

    ' Tile selection and display
    Private currentTileX As Integer = -1
    Private currentTileY As Integer = -1
    Private selectedTileImage As Bitmap = Nothing
    Private picTilePreview As New PictureBox()
    Private picTileOriginal As New PictureBox()

    ' View state
    Private zoomLevel As Single = 1.0F
    Private offsetX As Integer = 0
    Private offsetY As Integer = 0
    Private isDragging As Boolean = False
    Private lastMouseX As Integer = 0
    Private lastMouseY As Integer = 0
    Private showGrid As Boolean = False ' Default is grid off
    Private currentRenderMode As RenderMode = RenderMode.GameMap

    ' UI Controls
    Private fileMenu As New ToolStripMenuItem("File")       ' Menu Item 1
    Private editMenu As New ToolStripMenuItem("Edit")       ' Menu Item 2 
    Private viewMenu As New ToolStripMenuItem("View")       ' Menu Item 3
    Private renderMenu As New ToolStripMenuItem("Render")   ' Menu Item 4
    Private imageMenu As New ToolStripMenuItem("Image")     ' Menu Item 5
    Private helpMenu As New ToolStripMenuItem("Help")       ' Menu Item 6

    Private statusStrip As New StatusStrip()                ' Status bar (bottom of form)
    Private lblToolStripMapName As New ToolStripStatusLabel()
    Private lblToolStripMapPath As New ToolStripStatusLabel()
    Private lblToolStripCoordinates As New ToolStripStatusLabel()

    ' Undo/Redo stacks
    Private undoStack As New Stack(Of Command)
    Private redoStack As New Stack(Of Command)
    Private isUndoRedo As Boolean = False ' Flag to prevent undo/redo operations from being added to the stack
#End Region

#Region "Enumerations"
    ''' <summary>
    ''' Available map rendering modes
    ''' </summary>
    Private Enum RenderMode
        GameMap            ' 1  Game map view with well BMPs (default)
        CellTypeOverlay    ' 2  Cell types overlaid on game map
        CellTypeOverlayAll ' 3  Cell types with detailed text labels overlaid on game map
        CellTypeBasic      ' 4  Basic cell type coloring
        CellTypeAll        ' 5  Detailed cell type coloring
    End Enum
#End Region

#Region "Command Pattern Classes"
    ''' <summary>
    ''' Abstract Command class that defines the undo/redo interface
    ''' </summary>
    Public MustInherit Class Command
        Public MustOverride Sub Execute()
        Public MustOverride Sub Undo()
        Public MustOverride ReadOnly Property Description As String
    End Class

    ''' <summary>
    ''' Command for changing a tile's cell type
    ''' </summary>
    Public Class ChangeCellTypeCommand
        Inherits Command

        Private ReadOnly map As Map
        Private ReadOnly x As Integer
        Private ReadOnly y As Integer
        Private ReadOnly oldCellType As CellType
        Private ReadOnly newCellType As CellType

        Public Sub New(map As Map, x As Integer, y As Integer, oldCellType As CellType, newCellType As CellType)
            Me.map = map
            Me.x = x
            Me.y = y
            Me.oldCellType = oldCellType
            Me.newCellType = newCellType
        End Sub

        Public Overrides Sub Execute()
            map.SetCellType(newCellType, x, y)
        End Sub

        Public Overrides Sub Undo()
            map.SetCellType(oldCellType, x, y)
        End Sub

        Public Overrides ReadOnly Property Description As String
            Get
                Return $"Change cell type at ({x},{y}) from {oldCellType} to {newCellType}"
            End Get
        End Property
    End Class
#End Region

#Region "Form Initialization and Lifecycle"
    ''' <summary>
    ''' Handles form load event
    ''' </summary>
    Private Sub fMain_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Debug.WriteLine("--- " & ApplicationName & " Started ---")
        Text = ApplicationName
        Me.Icon = My.Resources.dataverse

        ShowLibraries()

        ' Initialize UI
        InitializeUI()

        ' Add tileset info labels
        AddTilesetInfoLabels()

        EditModeEnabled = False

        ' Check if OP2 path is set
        If String.IsNullOrEmpty(My.Settings.OP2Path) Then
            Debug.WriteLine("OP2Path not set. First Run.")
            MessageBox.Show("Welcome. First step is to browse for the Outpost 2 (1.4.1) folder. Click the browse button to locate the folder.", "Welcome", MessageBoxButtons.OK, MessageBoxIcon.Information)
            ShowSettingsDialog()

        Else
            Debug.WriteLine("OP2Path is set.")
            InitializeMapResourceManager()

        End If

        ' Process command line arguments
        Dim args = Environment.GetCommandLineArgs()
        If args.Length > 1 Then
            Dim filePath = args(1)

            ' Check if the file exists
            If File.Exists(filePath) Then
                ' Try to load the map file
                If LoadMapFile(filePath) Then
                    Debug.WriteLine(" - Map loaded successfully")
                Else
                    Debug.WriteLine(" - Failed to load map")
                End If
            Else
                Debug.Write(" - File not found")
            End If
        End If
    End Sub

    ''' <summary>
    ''' Show version numbers of the OP2 libraries
    ''' </summary>
    Private Sub ShowLibraries()
        Dim targetAssemblies = {"OP2UtilityDotNet", "OP2MapJsonTools"}
        Dim dummy1 = GetType(OP2UtilityDotNet.OP2Map.Map)       ' Force usage so assembly gets loaded
        Dim dummy2 = GetType(OP2MapJsonTools.JsonExportFormat)
        For Each asm As Assembly In AppDomain.CurrentDomain.GetAssemblies()
            Try
                Dim name = asm.GetName()
                If targetAssemblies.Contains(name.Name) Then
                    Debug.WriteLine($"{name.Name} - Version: {name.Version}")
                End If
            Catch ex As Exception
                ' Debug.WriteLine($"Error reading assembly info: {ex.Message}")
            End Try
        Next
    End Sub

    ''' <summary>
    ''' Initializes the OP2UtilityDotNet ResourceManager
    ''' </summary>
    Public Sub InitializeMapResourceManager()
        Try
            ' Initialize resource manager with the Outpost 2 game directory
            resourceManager = New ResourceManager(My.Settings.OP2Path)
        Catch ex As Exception
            Debug.WriteLine("InitializeMapResourceManager Error: " & ex.Message)
        End Try
    End Sub


    ''' <summary>
    ''' Initializes the user interface elements
    ''' </summary>
    Private Sub InitializeUI()
        ' Set up menu items
        Dim menuStrip As New MenuStrip()

        ' File menu setup
        fileMenu.DropDownItems.Add("Open Map", Nothing, AddressOf OpenMap_Click)
        'fileMenu.DropDownItems.Add("Save Map", Nothing, AddressOf SaveMap_Click)       'Viewer only app
        fileMenu.DropDownItems.Add("Close Map", Nothing, AddressOf CloseMap_Click)
        fileMenu.DropDownItems.Add("Settings", Nothing, AddressOf Settings_Click)
        fileMenu.DropDownItems.Add("-")
        fileMenu.DropDownItems.Add("Exit", Nothing, AddressOf Exit_Click)

        ' Edit menu setup
        editMenu.Name = "mnuEdit"
        editMenu.Enabled = False

        'Dim undoMenuItem As New ToolStripMenuItem("Undo")
        'undoMenuItem.Name = "mnuUndo"
        'undoMenuItem.ShortcutKeys = Keys.Control Or Keys.Z
        'undoMenuItem.Enabled = False
        'AddHandler undoMenuItem.Click, AddressOf Undo_Click

        'Dim redoMenuItem As New ToolStripMenuItem("Redo")
        'redoMenuItem.Name = "mnuRedo"
        'redoMenuItem.ShortcutKeys = Keys.Control Or Keys.Y
        'redoMenuItem.Enabled = False
        'AddHandler redoMenuItem.Click, AddressOf Redo_Click

        Dim jsonExportMenuItem As New ToolStripMenuItem("Export to JSON")
        jsonExportMenuItem.Name = "mnuJsonExport"
        'jsonExportMenuItem.Enabled = False
        AddHandler jsonExportMenuItem.Click, AddressOf ExportToJson_Click

        'editMenu.DropDownItems.Add(undoMenuItem)
        'editMenu.DropDownItems.Add(redoMenuItem)
        'editMenu.DropDownItems.Add("-")
        editMenu.DropDownItems.Add(jsonExportMenuItem)

        ' Render menu setup
        renderMenu.DropDownItems.Add("GameMap", Nothing, AddressOf RenderModeGameMap_Click)
        renderMenu.DropDownItems.Add("CellTypeOverlay", Nothing, AddressOf RenderModeCellTypeOverlay_Click)
        renderMenu.DropDownItems.Add("CellTypeOverlayAll", Nothing, AddressOf RenderModeCellTypeOverlayAll_Click)
        renderMenu.DropDownItems.Add("CellTypeBasic", Nothing, AddressOf RenderModeCellTypeBasic_Click)
        renderMenu.DropDownItems.Add("CellTypeAll", Nothing, AddressOf RenderModeCellTypeAll_Click)
        renderMenu.Enabled = False ' Disabled until a map is loaded

        ' View menu setup
        Dim gridMenuItem As New ToolStripMenuItem("Show Grid")
        gridMenuItem.Name = "mnuGrid"
        gridMenuItem.ShortcutKeys = Keys.Control Or Keys.G
        gridMenuItem.CheckOnClick = True ' Makes it toggle when clicked
        AddHandler gridMenuItem.Click, AddressOf ToggleGrid_Click
        viewMenu.DropDownItems.Add(gridMenuItem)

        ' Image menu setup
        Dim exportImageMenu As New ToolStripMenuItem("Export Image")
        exportImageMenu.DropDownItems.Add("Full Size", Nothing, AddressOf ExportImageFullSize_Click)
        exportImageMenu.DropDownItems.Add("1/8th Size", Nothing, AddressOf ExportImageEighthSize_Click)
        exportImageMenu.DropDownItems.Add("1/16th Size", Nothing, AddressOf ExportImageSixteenthSize_Click)

        imageMenu.DropDownItems.Add(exportImageMenu)
        imageMenu.DropDownItems.Add("-") ' Add separator
        imageMenu.DropDownItems.Add("Export Overview", Nothing, AddressOf ExportImageMenu_Click)
        imageMenu.Enabled = False ' Disabled until a map is loaded

        ' Add all menus to the menu strip
        menuStrip.Items.Add(fileMenu)
        menuStrip.Items.Add(editMenu)
        menuStrip.Items.Add(viewMenu)
        menuStrip.Items.Add(renderMenu)
        menuStrip.Items.Add(imageMenu)
        menuStrip.Items.Add(helpMenu)

        ' Status strip setup
        lblToolStripMapName.Name = "lblToolStripMapName"
        lblToolStripMapName.Text = ""
        statusStrip.Items.Add(lblToolStripMapName)

        lblToolStripMapPath.Name = "lblToolStripMapPath"
        lblToolStripMapPath.Text = "N/A"
        statusStrip.Items.Add(lblToolStripMapPath)

        lblToolStripCoordinates.Name = "lblCoordinates"
        lblToolStripCoordinates.Text = "0,0"
        statusStrip.Items.Add(lblToolStripCoordinates)

        ' Map panel setup
        pnlMap.Name = "pnlMap"
        pnlMap.Dock = DockStyle.Fill
        pnlMap.AutoScroll = True
        pnlMap.BackColor = System.Drawing.Color.DarkGray

        ' Enable double buffering for smooth rendering
        pnlMap.GetType().GetProperty("DoubleBuffered", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic).SetValue(pnlMap, True)

        ' Add event handlers
        AddHandler pnlMap.Paint, AddressOf MapPanel_Paint
        AddHandler pnlMap.MouseMove, AddressOf MapPanel_MouseMove
        AddHandler pnlMap.MouseDown, AddressOf MapPanel_MouseDown
        AddHandler pnlMap.MouseUp, AddressOf MapPanel_MouseUp
        AddHandler pnlMap.MouseWheel, AddressOf MapPanel_MouseWheel

        ' Properties panel setup
        pnlProperties.Name = "pnlProperties"
        pnlProperties.Dock = DockStyle.Right
        pnlProperties.Width = 240
        pnlProperties.BackColor = SystemColors.Control

        Dim lblPropertiesTitle As New Label()
        lblPropertiesTitle.Text = "Tile Properties"
        lblPropertiesTitle.Dock = DockStyle.Top
        lblPropertiesTitle.Font = New Font(lblPropertiesTitle.Font, FontStyle.Bold)
        lblPropertiesTitle.Height = 30
        lblPropertiesTitle.TextAlign = System.Drawing.ContentAlignment.MiddleCenter

        ' Add splitter
        Dim splitter As New Splitter()
        splitter.Dock = DockStyle.Right

        ' Add controls to form
        Controls.Add(pnlMap)
        Controls.Add(splitter)
        Controls.Add(pnlProperties)
        Controls.Add(statusStrip)
        Controls.Add(menuStrip)

        MainMenuStrip = menuStrip
    End Sub

    ''' <summary>
    ''' Adds property labels to the tile properties panel
    ''' </summary>
    Private Sub AddTilesetInfoLabels()
        ' Try to find the container for tile properties
        Dim propertyContainer As Control = Nothing

        ' Look for containers on the right side
        For Each ctrl As Control In Me.Controls
            If TypeOf ctrl Is Panel OrElse TypeOf ctrl Is GroupBox Then
                ' Check if this might be your property panel (on the right side)
                If ctrl.Dock = DockStyle.Right OrElse (ctrl.Left > Me.Width / 2 AndAlso ctrl.Width < Me.Width / 2) Then
                    propertyContainer = ctrl
                    Exit For
                End If
            End If
        Next

        ' If we couldn't find a container, we'll use the form itself
        If propertyContainer Is Nothing Then
            propertyContainer = Me
        End If

        ' Create and add the labels
        Dim yPos As Integer = 20  ' Starting position for map info
        Dim xPos As Integer = 30  ' Starting X position

        '1  lblMapName
        lblMapName.Name = "lblMapName"
        lblMapName.Text = "Map Name: N/A"
        lblMapName.AutoSize = True
        lblMapName.Location = New Point(10, yPos)
        propertyContainer.Controls.Add(lblMapName)

        '2  lblMapSize
        lblMapSize.Name = "lblMapSize"
        lblMapSize.Text = "Map Size: N/A"
        lblMapSize.AutoSize = True
        lblMapSize.Location = New Point(10, yPos + 20)
        propertyContainer.Controls.Add(lblMapSize)

        '3  lblMapImage
        lblMapImage.Name = "lblMapImage"
        lblMapImage.Text = "Map Image: N/A"
        lblMapImage.AutoSize = True
        lblMapImage.Location = New Point(10, yPos + 40)
        propertyContainer.Controls.Add(lblMapImage)

        '4  lblTileCount
        lblTileCount.Name = "lblTileCount"
        lblTileCount.Text = "Tile Count: N/A"
        lblTileCount.AutoSize = True
        lblTileCount.Location = New Point(10, yPos + 60)
        propertyContainer.Controls.Add(lblTileCount)

        '5  lblTileset
        lblTileset.Name = "lblTileset"
        lblTileset.Text = "Tileset: N/A"
        lblTileset.AutoSize = True
        lblTileset.Location = New Point(10, yPos + 80)
        propertyContainer.Controls.Add(lblTileset)

        '6  lblTilesetImage
        lblTilesetImage.Name = "lblTilesetImage"
        lblTilesetImage.Text = "Image Index: N/A"
        lblTilesetImage.AutoSize = True
        lblTilesetImage.Location = New Point(10, yPos + 100)
        propertyContainer.Controls.Add(lblTilesetImage)

        '7  lblMappingIndex
        lblMappingIndex.Name = "lblMappingIndex"
        lblMappingIndex.Text = "Mapping Index: N/A"
        lblMappingIndex.AutoSize = True
        lblMappingIndex.Location = New Point(10, yPos + 120)
        propertyContainer.Controls.Add(lblMappingIndex)

        '8  lblCoordinates'
        lblCoordinates.Name = "lblCoordinates"
        lblCoordinates.Text = "Coordinates: N/A"
        lblCoordinates.AutoSize = True
        lblCoordinates.Location = New Point(10, yPos + 140)
        propertyContainer.Controls.Add(lblCoordinates)

        '9  lblCellType
        lblCellType.Name = "lblCellType"
        lblCellType.Text = "Cell Type: N/A"
        lblCellType.AutoSize = True
        lblCellType.Location = New Point(10, yPos + 160)
        propertyContainer.Controls.Add(lblCellType)

        '10  lblZoomLevel
        lblZoomLevel.Name = "lblZoomLevel"
        lblZoomLevel.Text = "Zoom Level: N/A"
        lblZoomLevel.AutoSize = True
        lblZoomLevel.Location = New Point(10, yPos + 220)
        propertyContainer.Controls.Add(lblZoomLevel)

        ' Add Reset Zoom button next to Zoom Level label
        Dim btnResetZoom As New Button()
        btnResetZoom.Name = "btnResetZoom"
        btnResetZoom.Text = "Reset Zoom"
        btnResetZoom.Size = New Size(80, 23)
        btnResetZoom.Location = New Point(lblZoomLevel.Location.X + 120, lblZoomLevel.Location.Y - 2)
        btnResetZoom.Enabled = False ' Disabled until map is loaded
        AddHandler btnResetZoom.Click, AddressOf ResetZoom_Click
        propertyContainer.Controls.Add(btnResetZoom)

        ' Add Center View button below Reset Zoom button
        Dim btnCenterView As New Button()
        btnCenterView.Name = "btnCenterView"
        btnCenterView.Text = "Center View"
        btnCenterView.Size = New Size(80, 23)
        btnCenterView.Location = New Point(lblZoomLevel.Location.X + 120, lblZoomLevel.Location.Y + 25)
        btnCenterView.Enabled = False ' Disabled until map is loaded
        AddHandler btnCenterView.Click, AddressOf CenterView_Click
        propertyContainer.Controls.Add(btnCenterView)

        ' Add a label for the tile preview
        Dim lblTilePreview As New Label()
        lblTilePreview.Text = "Tile Preview:"
        lblTilePreview.AutoSize = True
        lblTilePreview.Location = New Point(10, yPos + 285)
        propertyContainer.Controls.Add(lblTilePreview)

        ' Add the original size tile preview
        picTileOriginal.Name = "picTileOriginal"
        picTileOriginal.Size = New Size(32, 32)  ' Original tile size
        picTileOriginal.Location = New Point(20, yPos + 310)
        picTileOriginal.BorderStyle = BorderStyle.FixedSingle
        picTileOriginal.SizeMode = PictureBoxSizeMode.Normal
        picTileOriginal.BackColor = System.Drawing.Color.Black
        propertyContainer.Controls.Add(picTileOriginal)

        ' Add the PictureBox for enlarged tile preview
        picTilePreview.Name = "picTilePreview"
        picTilePreview.Size = New Size(96, 96)  ' 3x the tile size for better visibility
        picTilePreview.Location = New Point(70, yPos + 310)
        picTilePreview.BorderStyle = BorderStyle.FixedSingle
        picTilePreview.SizeMode = PictureBoxSizeMode.StretchImage
        picTilePreview.BackColor = System.Drawing.Color.Black
        propertyContainer.Controls.Add(picTilePreview)
    End Sub

    ''' <summary>
    ''' Shows the settings dialog to configure application settings
    ''' </summary>
    Private Sub ShowSettingsDialog()
        'fSettings.Show()
        'fSettings.ShowDialog()

        'Dim settings As New fSettings()
        'If settings.ShowDialog() = DialogResult.OK Then
        '    Try
        '        ' Initialize resource manager with the Outpost 2 game directory
        '        resourceManager = New ResourceManager(My.Settings.OP2Path)
        '    Catch ex As Exception
        '        MessageBox.Show("Error initializing resource manager: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        '    End Try
        'End If

        Using settings As New fSettings()
            ' Show the dialog modally - blocks until user closes the form
            Dim result As DialogResult = settings.ShowDialog()

            ' Only proceed if the user clicked Save/OK
            If result = DialogResult.OK Then
                Debug.WriteLine("Settings saved. Attempting to initialize resource manager.")

                ' Try to initialize the resource manager with the new settings
                Try
                    InitializeMapResourceManager()
                    Debug.WriteLine("Resource manager initialized successfully.")
                Catch ex As Exception
                    Debug.WriteLine("Error initializing resource manager: " & ex.Message)
                    MessageBox.Show("The settings were saved, but there was an error initializing the game resources: " &
                                ex.Message & vbCrLf & vbCrLf &
                                "Map display may be limited to basic rendering.",
                                "Resource Error", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End Try
            Else
                Debug.WriteLine("Settings dialog was canceled. No changes applied.")
            End If
        End Using

    End Sub

    ''' <summary>
    ''' Handles form closing event, performs cleanup
    ''' </summary>
    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        ' Clean up resources
        CleanupMapResources()

        If resourceManager IsNot Nothing Then
            resourceManager.Dispose()
        End If

        MyBase.OnFormClosing(e)
    End Sub

    ''' <summary>
    ''' Processes command key presses for handling hotkeys
    ''' </summary>
    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        ' Check for Ctrl+G (Toggle Grid)
        If keyData = (Keys.Control Or Keys.G) Then
            ' Find and click the menu item to toggle grid
            Dim menuItem As ToolStripMenuItem = DirectCast(MainMenuStrip.Items.Find("mnuGrid", True)(0), ToolStripMenuItem)
            menuItem.PerformClick()
            Return True
        End If

        ' Let the base class handle the rest
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function
#End Region

#Region "Map File Operations"
    ''' <summary>
    ''' Loads a map file from the specified path
    ''' </summary>
    ''' <param name="mapFilePath">Path to the map file</param>
    ''' <returns>True if loading was successful, false otherwise</returns>
    Private Function LoadMapFile(mapFilePath As String) As Boolean
        ' Check if resource manager is initialized
        If resourceManager Is Nothing Then
            If String.IsNullOrEmpty(My.Settings.WorkingPath) OrElse Not Directory.Exists(My.Settings.WorkingPath) Then
                ShowSettingsDialog()
                If resourceManager Is Nothing Then
                    Return False
                End If
            Else
                Try
                    resourceManager = New ResourceManager(My.Settings.WorkingPath)
                Catch ex As Exception
                    MessageBox.Show("Error initializing resource manager: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Return False
                End Try
            End If
        End If

        Try
            ' Load the map file
            currentMap = Map.ReadMap(mapFilePath)
            currentMapFile = Path.GetFileName(mapFilePath)
            currentMapPath = Path.GetDirectoryName(mapFilePath)

            LoadMapTilesets()

            ShowTilesetLoadingStatus()

            ' Set the default render mode to GameMap
            currentRenderMode = RenderMode.GameMap

            GenerateMapBitmap()

            UpdateMapLabelsOnLoad()

            ' Reset view
            zoomLevel = 1.0F
            offsetX = 0
            offsetY = 0

            ' Enable save and close options
            Dim saveMenuItem As ToolStripMenuItem = DirectCast(DirectCast(MainMenuStrip.Items(0), ToolStripMenuItem).DropDownItems(1), ToolStripMenuItem)
            saveMenuItem.Enabled = True

            Dim closeMenuItem As ToolStripMenuItem = DirectCast(DirectCast(MainMenuStrip.Items(0), ToolStripMenuItem).DropDownItems(2), ToolStripMenuItem)
            closeMenuItem.Enabled = True

            ' Enable edit + render + image menu's
            editMenu.Enabled = True
            renderMenu.Enabled = True
            imageMenu.Enabled = True

            ' Enable the zoom and center buttons
            Dim btnResetZoom As Button = DirectCast(FindControl("btnResetZoom"), Button)
            If btnResetZoom IsNot Nothing Then
                btnResetZoom.Enabled = True
            End If

            Dim btnCenterView As Button = DirectCast(FindControl("btnCenterView"), Button)
            If btnCenterView IsNot Nothing Then
                btnCenterView.Enabled = True
            End If

            ' Update UI
            FindControl("pnlMap").Invalidate()

            ' Check the map if it has corrupt tile group names - OP2Mapper2 would corrupt them
            If HasCorruptTileGroupNames() = True Then
                Debug.WriteLine("The map's tile group names are corrupt.")
            End If

            Return True

        Catch ex As Exception
            MessageBox.Show("Error loading map: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Debug.WriteLine("Error loading map: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)

            Return False
        End Try
    End Function

    ''' <summary>
    ''' Updates all UI labels with information about the loaded map
    ''' </summary>
    Private Sub UpdateMapLabelsOnLoad()
        ' Update the form title
        Text = ApplicationName & " - " & currentMapFile

        ' Update status bar labels
        lblToolStripMapName.Text = currentMapFile
        lblToolStripMapPath.Text = Path.Combine(currentMapPath, currentMapFile)  ' Properly joins path components

        ' Update property panel map information
        lblMapName.Text = "Map Name: " & currentMapFile
        lblMapSize.Text = "Map Size: " & currentMap.WidthInTiles & " x " & currentMap.HeightInTiles

        ' Add the map image size information
        Dim mapWidthPixels As Integer = CInt(currentMap.WidthInTiles()) * 32
        Dim mapHeightPixels As Integer = CInt(currentMap.HeightInTiles()) * 32
        lblMapImage.Text = "Map Image: " & mapWidthPixels & " x " & mapHeightPixels & " pixels"

        ' Update additional map statistics
        lblTileCount.Text = "Tile Count: " & currentMap.TileCount

        ' Set default zoom level
        lblZoomLevel.Text = "Zoom Level: 100%"

        ' Update the coordinates in status bar
        lblToolStripCoordinates.Text = "Coordinates: 0, 0"
    End Sub

    ''' <summary>
    ''' Handles Open Map menu item click event
    ''' </summary>
    Private Sub OpenMap_Click(sender As Object, e As EventArgs)
        ' Open map file dialog
        Using openDialog As New OpenFileDialog()
            openDialog.Filter = "Outpost 2 Map Files (*.map)|*.map|All Files (*.*)|*.*"
            openDialog.Title = "Open Outpost 2 Map"
            openDialog.InitialDirectory = My.Settings.OP2Path
            'openDialog.InitialDirectory = My.Settings.WorkingPath

            If openDialog.ShowDialog() = DialogResult.OK Then
                If LoadMapFile(openDialog.FileName) Then
                    ' Show message that map has been loaded
                    MessageBox.Show("Map loaded successfully.", "Map Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information)
                End If
            End If
        End Using
    End Sub

    ''' <summary>
    ''' Handles Save Map menu item click event
    ''' </summary>
    Private Sub SaveMap_Click(sender As Object, e As EventArgs)
        If currentMap Is Nothing Then
            Return
        End If

        Using saveDialog As New SaveFileDialog()
            saveDialog.Filter = "Outpost 2 Map Files (*.map)|*.map|All Files (*.*)|*.*"
            saveDialog.Title = "Save Outpost 2 Map"

            If saveDialog.ShowDialog() = DialogResult.OK Then
                Try
                    currentMap.Write(saveDialog.FileName)
                    MessageBox.Show("Map saved successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Text = ApplicationName & " - " & Path.GetFileName(saveDialog.FileName)
                Catch ex As Exception
                    MessageBox.Show("Error saving map: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                End Try
            End If
        End Using
    End Sub

    ''' <summary>
    ''' Handles Close Map menu item click event
    ''' </summary>
    Private Sub CloseMap_Click(sender As Object, e As EventArgs)
        If currentMap Is Nothing Then
            Return
        End If

        ' Confirm with user if they want to close the current map
        If MessageBox.Show("Close the current map?", "Close Map", MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
            For Each form As Form In Application.OpenForms
                If TypeOf form Is fCellTypes Then
                    form.Close()
                    Exit For
                End If
            Next

            ' Clean up resources
            CleanupMapResources()

            ' Reset UI
            Text = ApplicationName
            lblToolStripMapName.Text = ""
            lblToolStripMapPath.Text = ""
            lblToolStripCoordinates.Text = "0,0"

            lblMapName.Text = "Map Name: N/A"
            lblMapSize.Text = "Map Size: N/A"
            lblMapImage.Text = "Map Image: N/A"
            lblTileCount.Text = "Tile Count: N/A"
            lblTileset.Text = "Tileset: N/A"
            lblTilesetImage.Text = "Image Index: N/A"
            lblMappingIndex.Text = "Mapping Index: N/A"
            lblCoordinates.Text = "Coordinates: N/A"
            lblCellType.Text = "Cell Type: N/A"
            lblZoomLevel.Text = "Zoom Level: N/A"

            ' Clear tile previews
            picTilePreview.Image = Nothing
            picTileOriginal.Image = Nothing

            ' Disable menu options
            Dim saveMenuItem As ToolStripMenuItem = DirectCast(DirectCast(MainMenuStrip.Items(0), ToolStripMenuItem).DropDownItems(1), ToolStripMenuItem)
            saveMenuItem.Enabled = False

            Dim closeMenuItem As ToolStripMenuItem = DirectCast(DirectCast(MainMenuStrip.Items(0), ToolStripMenuItem).DropDownItems(2), ToolStripMenuItem)
            closeMenuItem.Enabled = False

            ' Disable edit + render + image menu's
            editMenu.Enabled = False
            renderMenu.Enabled = False
            imageMenu.Enabled = False

            ' Disable the zoom and center buttons
            Dim btnResetZoom As Button = DirectCast(FindControl("btnResetZoom"), Button)
            If btnResetZoom IsNot Nothing Then
                btnResetZoom.Enabled = False
            End If

            Dim btnCenterView As Button = DirectCast(FindControl("btnCenterView"), Button)
            If btnCenterView IsNot Nothing Then
                btnCenterView.Enabled = False
            End If

            EditModeEnabled = False

            ' Refresh display
            FindControl("pnlMap").Invalidate()
        End If
    End Sub

    ''' <summary>
    ''' Cleans up map resources when closing a map
    ''' </summary>
    Private Sub CleanupMapResources()
        ' Clean up the current map and associated resources
        currentMap = Nothing
        currentMapFile = Nothing
        currentMapPath = Nothing

        ' Clean up image resources
        If mapBitmap IsNot Nothing Then
            mapBitmap.Dispose()
            mapBitmap = Nothing
        End If

        If selectedTileImage IsNot Nothing Then
            selectedTileImage.Dispose()
            selectedTileImage = Nothing
        End If

        ' Clean up tileset images
        For Each bitmapList In tilesetImages.Values
            For Each bmp In bitmapList
                If bmp IsNot Nothing Then
                    bmp.Dispose()
                End If
            Next
        Next
        tilesetImages.Clear()

        ' Reset selection
        currentTileX = -1
        currentTileY = -1
    End Sub

    ''' <summary>
    ''' Exports the current map to JSON format
    ''' </summary>
    Private Sub ExportToJson_Click(sender As Object, e As EventArgs)
        ExportMapToJson()
    End Sub

    ''' <summary>
    ''' Exports the current map to JSON format
    ''' </summary>
    Private Sub ExportMapToJson()
        If currentMap Is Nothing Then
            MessageBox.Show("Please load a map first.", "No Map Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim MapAuthor As String = "Unknown"
        Dim MapNotes As String = "N/A"

        Dim ExportFormat As JsonExportFormat
        ' JsonExportFormat.Original     - Flat arrays like the C++ implementation
        ' JsonExportFormat.PerRow       - Each row as a separate array on a single line
        ' JsonExportFormat.PerRowPadded - Same as PerRow but with padded numbers for alignment
        ExportFormat = JsonExportFormat.PerRowPadded

        Using saveDialog As New SaveFileDialog()
            saveDialog.Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*"
            saveDialog.Title = "Export Map to JSON"
            saveDialog.FileName = Path.GetFileNameWithoutExtension(currentMapFile) & ".json"
            If saveDialog.ShowDialog() = DialogResult.OK Then
                Try
                    Cursor = Cursors.WaitCursor

                    ' Pass the currentMapFile for both map name and map file name
                    If Not String.IsNullOrEmpty(currentMapPath) Then
                        OP2MapJsonTools.ExportMapToJsonFile(Path.Combine(currentMapPath, currentMapFile), saveDialog.FileName, ExportFormat, currentMapFile, MapAuthor, MapNotes)
                    Else
                        OP2MapJsonTools.ExportMapToJsonFile(currentMapFile, saveDialog.FileName, ExportFormat, currentMapFile, MapAuthor, MapNotes)
                    End If

                    MessageBox.Show("Map exported to JSON successfully!", "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information)

                    ' Ask if the user wants to open the file
                    If MessageBox.Show("Do you want to open the exported JSON file?", "Open File", MessageBoxButtons.YesNo, MessageBoxIcon.Question) = DialogResult.Yes Then
                        Process.Start(saveDialog.FileName)
                    End If
                Catch ex As Exception
                    MessageBox.Show("Error exporting map to JSON: " & ex.Message, "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Finally
                    Cursor = Cursors.Default
                End Try
            End If
        End Using
    End Sub
#End Region

#Region "Menu Event Handlers"
    ''' <summary>
    ''' Handles Settings menu item click event
    ''' </summary>
    Private Sub Settings_Click(sender As Object, e As EventArgs)
        ShowSettingsDialog()
    End Sub

    ''' <summary>
    ''' Handles Exit menu item click event
    ''' </summary>
    Private Sub Exit_Click(sender As Object, e As EventArgs)
        Close()
    End Sub

    ''' <summary>
    ''' Toggles grid display
    ''' </summary>
    Private Sub ToggleGrid_Click(sender As Object, e As EventArgs)
        ' Toggle grid display
        showGrid = Not showGrid

        ' Update menu item checked state
        Dim menuItem As ToolStripMenuItem = DirectCast(sender, ToolStripMenuItem)
        menuItem.Checked = showGrid

        ' Refresh the map display
        FindControl("pnlMap").Invalidate()
    End Sub

    ''' <summary>
    ''' Resets zoom level to 100%
    ''' </summary>
    Private Sub ResetZoom_Click(sender As Object, e As EventArgs)
        If currentMap Is Nothing Then
            Return
        End If

        ' Reset zoom to 100%
        zoomLevel = 1.0F
        offsetX = 0
        offsetY = 0

        ' Update zoom level label
        lblZoomLevel.Text = "Zoom Level: 100%"

        ' Refresh display
        FindControl("pnlMap").Invalidate()
    End Sub

    ''' <summary>
    ''' Centers the map view in the panel
    ''' </summary>
    Private Sub CenterView_Click(sender As Object, e As EventArgs)
        If currentMap Is Nothing Then
            Return
        End If

        ' Calculate map dimensions
        Dim mapWidth As Integer = CInt(currentMap.WidthInTiles()) * 32
        Dim mapHeight As Integer = CInt(currentMap.HeightInTiles()) * 32

        ' Calculate scaled map dimensions based on current zoom
        Dim scaledWidth As Integer = CInt(mapWidth * zoomLevel)
        Dim scaledHeight As Integer = CInt(mapHeight * zoomLevel)

        ' Get the panel's visible area
        Dim panelWidth As Integer = pnlMap.ClientSize.Width
        Dim panelHeight As Integer = pnlMap.ClientSize.Height

        ' Calculate offsets to center the map
        offsetX = (panelWidth - scaledWidth) \ 2
        offsetY = (panelHeight - scaledHeight) \ 2

        ' Ensure map is visible if it's smaller than the panel
        If scaledWidth < panelWidth Then
            offsetX = Math.Max(0, offsetX)
        End If

        If scaledHeight < panelHeight Then
            offsetY = Math.Max(0, offsetY)
        End If

        ' Refresh display
        FindControl("pnlMap").Invalidate()
    End Sub

    ''' <summary>
    ''' Sets the render mode to GameMap
    ''' </summary>
    Private Sub RenderModeGameMap_Click(sender As Object, e As EventArgs)
        '1  Render      GameMap
        If currentRenderMode <> RenderMode.GameMap Then
            currentRenderMode = RenderMode.GameMap
            GenerateMapBitmap()
            FindControl("pnlMap").Invalidate()
        End If
    End Sub

    ''' <summary>
    ''' Sets the render mode to CellTypeOverlay
    ''' </summary>
    Private Sub RenderModeCellTypeOverlay_Click(sender As Object, e As EventArgs)
        '2  Render      CellTypeOverlay
        If currentRenderMode <> RenderMode.CellTypeOverlay Then
            currentRenderMode = RenderMode.CellTypeOverlay
            GenerateMapBitmap()
            FindControl("pnlMap").Invalidate()
        End If
    End Sub

    ''' <summary>
    ''' Sets the render mode to CellTypeOverlayAll
    ''' </summary>
    Private Sub RenderModeCellTypeOverlayAll_Click(sender As Object, e As EventArgs)
        '3  Render  CellTypeOverlayAll
        If currentRenderMode <> RenderMode.CellTypeOverlayAll Then
            currentRenderMode = RenderMode.CellTypeOverlayAll
            GenerateMapBitmap()
            FindControl("pnlMap").Invalidate()
        End If
    End Sub

    ''' <summary>
    ''' Sets the render mode to CellTypeBasic
    ''' </summary>
    Private Sub RenderModeCellTypeBasic_Click(sender As Object, e As EventArgs)
        '4  Render  CellTypeBasic
        If currentRenderMode <> RenderMode.CellTypeBasic Then
            currentRenderMode = RenderMode.CellTypeBasic
            GenerateMapBitmap()
            FindControl("pnlMap").Invalidate()
        End If
    End Sub

    ''' <summary>
    ''' Sets the render mode to CellTypeAll
    ''' </summary>
    Private Sub RenderModeCellTypeAll_Click(sender As Object, e As EventArgs)
        '5  Render      CellTypeAll
        If currentRenderMode <> RenderMode.CellTypeAll Then
            currentRenderMode = RenderMode.CellTypeAll
            GenerateMapBitmap()
            FindControl("pnlMap").Invalidate()
        End If
    End Sub
#End Region

#Region "Image Export"
    ''' <summary>
    ''' Exports the map image at full size
    ''' </summary>
    Private Sub ExportImageFullSize_Click(sender As Object, e As EventArgs)
        If currentMap Is Nothing Then
            Return
        End If

        Try
            ' Create a bitmap of the exact size to match the map (without scaling)
            Dim exportWidth As Integer = CInt(currentMap.WidthInTiles()) * 32
            Dim exportHeight As Integer = CInt(currentMap.HeightInTiles()) * 32

            Dim exportBitmap As New Bitmap(exportWidth, exportHeight)

            ' Draw the map exactly as it would appear in our current render mode
            Using g As Graphics = Graphics.FromImage(exportBitmap)
                ' Draw the map image
                g.DrawImage(mapBitmap, 0, 0, exportWidth, exportHeight)
            End Using

            ' Generate filename in the same directory as the map file
            Dim exportFilename As String = Path.Combine(currentMapPath, Path.GetFileNameWithoutExtension(currentMapFile) & "_" & currentRenderMode.ToString() & ".jpg")

            ' Save as JPEG
            exportBitmap.Save(exportFilename, System.Drawing.Imaging.ImageFormat.Jpeg)

            ' Dispose of the bitmap
            exportBitmap.Dispose()

            ' Show success message
            MessageBox.Show("Map image exported successfully to: " & exportFilename, "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information)

        Catch ex As Exception
            MessageBox.Show("Error exporting image: " & ex.Message, "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>
    ''' Exports the map image at 1/8th size
    ''' </summary>
    Private Sub ExportImageEighthSize_Click(sender As Object, e As EventArgs)
        If currentMap Is Nothing Then
            Return
        End If

        ' Call the new function with 0.125 (12.5%) size
        ExportImageCustomSize(0.125, "small")
    End Sub

    ''' <summary>
    ''' Exports the map image at 1/16th size
    ''' </summary>
    Private Sub ExportImageSixteenthSize_Click(sender As Object, e As EventArgs)
        If currentMap Is Nothing Then
            Return
        End If

        ' Call the new function with 0.0625) (6.25%) size
        ExportImageCustomSize(0.0625, "mini")
    End Sub

    ''' <summary>
    ''' Exports the map image at a custom scale
    ''' </summary>
    ''' <param name="scale">Scale factor for export (0-1)</param>
    ''' <param name="filenameSuffix">Suffix to add to the filename</param>
    Private Sub ExportImageCustomSize(scale As Double, filenameSuffix As String)
        If currentMap Is Nothing Then
            Return
        End If

        Try
            ' Calculate the original and scaled dimensions
            Dim originalWidth As Integer = CInt(currentMap.WidthInTiles()) * 32
            Dim originalHeight As Integer = CInt(currentMap.HeightInTiles()) * 32

            ' Calculate dimensions based on provided scale
            Dim exportWidth As Integer = CInt(originalWidth * scale)
            Dim exportHeight As Integer = CInt(originalHeight * scale)

            ' Create smaller bitmap for export
            Dim exportBitmap As New Bitmap(exportWidth, exportHeight)

            ' Draw the scaled map
            Using g As Graphics = Graphics.FromImage(exportBitmap)
                ' Set high quality mode for resizing
                g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
                g.SmoothingMode = Drawing2D.SmoothingMode.HighQuality

                ' Draw the map image scaled down
                g.DrawImage(mapBitmap, 0, 0, exportWidth, exportHeight)
            End Using

            ' Generate filename in the same directory as the map file
            Dim scalePercent As Integer = CInt(scale * 100)
            Dim exportFilename As String = Path.Combine(currentMapPath, Path.GetFileNameWithoutExtension(currentMapFile) & "_" & currentRenderMode.ToString() & "_" & filenameSuffix & ".jpg")

            ' Check if file already exists
            If File.Exists(exportFilename) Then
                Debug.WriteLine($"File already exists, deleting: {exportFilename}")
                File.Delete(exportFilename)
            End If

            ' Save as JPEG
            exportBitmap.Save(exportFilename, System.Drawing.Imaging.ImageFormat.Jpeg)

            ' Dispose of the bitmap
            exportBitmap.Dispose()

            ' Show success message
            MessageBox.Show("Map image exported successfully at " & scalePercent & "% size to: " & exportFilename, "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information)
        Catch ex As Exception
            MessageBox.Show("Error exporting image: " & ex.Message, "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>
    ''' Handles "Export Overview" menu item click
    ''' </summary>
    Private Sub ExportImageMenu_Click(sender As Object, e As EventArgs)
        ' Export Overview menu item
        ExportOverviewImage()
    End Sub

    ''' <summary>
    ''' Exports an overview image with multiple render modes
    ''' </summary>
    Private Sub ExportOverviewImage()
        If currentMap Is Nothing Then
            MessageBox.Show("Please load a map first.", "No Map Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Try
            ' Create a progress form
            Dim progressForm As New Form()
            progressForm.Text = "Generating Overview..."
            progressForm.StartPosition = FormStartPosition.CenterParent
            progressForm.Size = New Size(300, 100)
            progressForm.FormBorderStyle = FormBorderStyle.FixedDialog
            progressForm.ControlBox = False

            Dim lblStatus As New Label()
            lblStatus.Text = "Creating overview image..."
            lblStatus.Dock = DockStyle.Fill
            progressForm.Controls.Add(lblStatus)

            progressForm.Show(Me)
            Application.DoEvents()

            ' Remember original render mode
            Dim originalRenderMode As RenderMode = currentRenderMode

            ' Define scale for small images
            Dim scale As Double = 0.125 ' 12.5% size

            ' Generate output filename for the overview image
            Dim overviewFilename As String = Path.Combine(currentMapPath, Path.GetFileNameWithoutExtension(currentMapFile) & "_overview.jpg")

            ' Check if overview file already exists and delete it if it does
            If File.Exists(overviewFilename) Then
                Debug.WriteLine($"Overview file already exists, deleting: {overviewFilename}")
                File.Delete(overviewFilename)
            End If

            ' Array of render modes to include
            Dim renderModes As RenderMode() = {
                RenderMode.GameMap,
                RenderMode.CellTypeOverlay,
                RenderMode.CellTypeOverlayAll,
                RenderMode.CellTypeBasic,
                RenderMode.CellTypeAll
            }

            ' Generate filenames for each render mode image
            Dim modeFilenames As New List(Of String)()

            ' Generate each render mode image using the existing ExportImageCustomSize method
            For Each mode In renderModes
                lblStatus.Text = $"Rendering {mode}..."
                Application.DoEvents()

                ' Switch to this render mode
                currentRenderMode = mode
                GenerateMapBitmap()

                ' Generate filename suffix based on render mode
                Dim modeSuffix As String = "_" & mode.ToString() & "_small"

                ' Export this render mode to disk using existing method
                ExportImageCustomSize(scale, "small")

                ' Store the filename that was generated
                Dim modeFilename As String = Path.Combine(currentMapPath, Path.GetFileNameWithoutExtension(currentMapFile) & "_" & mode.ToString() & "_small.jpg")
                modeFilenames.Add(modeFilename)
            Next

            ' Now create the composite overview image
            lblStatus.Text = "Creating overview image..."
            Application.DoEvents()

            ' Load the individual mode images
            Dim modeImages As New List(Of Bitmap)()
            For Each filename In modeFilenames
                If File.Exists(filename) Then
                    modeImages.Add(New Bitmap(filename))
                Else
                    Throw New FileNotFoundException($"Cannot find render mode image: {filename}")
                End If
            Next

            ' Get dimensions of the first image to calculate layout
            Dim smallImageWidth As Integer = modeImages(0).Width
            Dim smallImageHeight As Integer = modeImages(0).Height

            ' Define padding
            Const padding As Integer = 6

            ' Calculate total image dimensions (3x2 grid with padding)
            Dim totalWidth As Integer = smallImageWidth * 3 + padding * 4
            Dim totalHeight As Integer = smallImageHeight * 2 + padding * 3

            ' Create the composite image
            Using compositeImage As New Bitmap(totalWidth, totalHeight)
                Using g As Graphics = Graphics.FromImage(compositeImage)
                    ' Fill background with white
                    g.Clear(System.Drawing.Color.White)  ' White background

                    ' Calculate positions for each image in the grid
                    Dim positions As Point() = {
                        New Point(padding + smallImageWidth + padding, padding),                                ' Top-middle (GameMap)
                        New Point(padding + smallImageWidth + padding + smallImageWidth + padding, padding),    ' Top-right (CellTypeOverlay)
                        New Point(padding, padding + smallImageHeight + padding),                               ' Bottom-left (CellTypeOverlayAll)
                        New Point(padding + smallImageWidth + padding, padding + smallImageHeight + padding),   ' Bottom-middle (CellTypeBasic)
                        New Point(padding + smallImageWidth + padding + smallImageWidth + padding, padding + smallImageHeight + padding) ' Bottom-right (CellTypeAll)
                    }

                    ' Place the images in the grid
                    For i As Integer = 0 To modeImages.Count - 1
                        ' Draw border around the image - White
                        g.FillRectangle(Brushes.White, positions(i).X - 1, positions(i).Y - 1, smallImageWidth + 2, smallImageHeight + 2)
                        ' Draw the image
                        g.DrawImage(modeImages(i), positions(i))
                    Next

                    ' Create the info panel for the top-left cell
                    Using infoPanel As New Bitmap(smallImageWidth, smallImageHeight)
                        Using infoG As Graphics = Graphics.FromImage(infoPanel)
                            ' Set up for info panel drawing
                            infoG.Clear(System.Drawing.Color.FromArgb(240, 240, 240))  ' Light gray background
                            infoG.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

                            ' Draw border
                            infoG.DrawRectangle(New Pen(System.Drawing.Color.DarkGray, 1), 0, 0, smallImageWidth - 1, smallImageHeight - 1)

                            ' Draw info text 
                            Using titleFont As New Font("Arial", 16, FontStyle.Bold)
                                Using normalFont As New Font("Arial", 12)
                                    ' Draw title
                                    infoG.DrawString("Map Overview", titleFont, Brushes.Black, 10, 10)

                                    ' Draw map info
                                    Dim y As Integer = 36  ' Adjusted for larger font
                                    infoG.DrawString("Map: " & Path.GetFileName(currentMapFile), normalFont, Brushes.Black, 10, y)
                                    y += 20  ' Increased spacing
                                    infoG.DrawString("Size: " & currentMap.WidthInTiles() & " x " & currentMap.HeightInTiles(), normalFont, Brushes.Black, 10, y)
                                    y += 20
                                    infoG.DrawString("Tiles: " & currentMap.TileCount().ToString("#,##0"), normalFont, Brushes.Black, 10, y)

                                    y += 36

                                    infoG.DrawString("Render Modes:", normalFont, Brushes.Black, 10, y)
                                    y += 20

                                    ' Draw render mode list
                                    Dim modeDescriptions As String() = {
                                        "1: Game Map (top-middle)",
                                        "2: CellType Overlay (top-right)",
                                        "3: CellType Overlay All (bottom-left)",
                                        "4: CellType Basic (bottom-middle)",
                                        "5: CellType All (bottom-right)"
                                    }

                                    For Each desc In modeDescriptions
                                        infoG.DrawString(desc, normalFont, Brushes.Black, 10, y)
                                        y += 20  ' Increased spacing
                                    Next

                                    ' Add generation date at bottom
                                    infoG.DrawString("Generated: " & DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"), normalFont, Brushes.Black, 10, smallImageHeight - 30)
                                End Using
                            End Using
                        End Using

                        ' Draw WHITE border around the info panel
                        g.FillRectangle(Brushes.White, padding - 1, padding - 1, smallImageWidth + 2, smallImageHeight + 2)
                        ' Add the info panel to the composite image
                        g.DrawImage(infoPanel, padding, padding)
                    End Using
                End Using

                ' Save as JPEG with high quality
                Dim jpegEncoder As ImageCodecInfo = GetEncoder(ImageFormat.Jpeg)
                Dim encoderParameters As New EncoderParameters(1)
                encoderParameters.Param(0) = New EncoderParameter(Encoder.Quality, 95L)
                compositeImage.Save(overviewFilename, jpegEncoder, encoderParameters)
            End Using

            ' Dispose all loaded images
            For Each img In modeImages
                img.Dispose()
            Next

            ' Restore original render mode
            currentRenderMode = originalRenderMode
            GenerateMapBitmap()
            FindControl("pnlMap").Invalidate()

            ' Close progress form
            progressForm.Close()

            ' Show success message
            MessageBox.Show("Overview image exported successfully to: " & overviewFilename, "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information)

            ' Open the image with the default viewer
            Process.Start(overviewFilename)

        Catch ex As Exception
            MessageBox.Show("Error exporting overview image: " & ex.Message, "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Debug.WriteLine($"Export Overview error: {ex}")
        End Try
    End Sub

    ''' <summary>
    ''' Helper function to get the JPEG encoder
    ''' </summary>
    ''' <param name="format">Image format to get encoder for</param>
    ''' <returns>ImageCodecInfo for the requested format</returns>
    Private Function GetEncoder(format As ImageFormat) As ImageCodecInfo
        Dim codecs As ImageCodecInfo() = ImageCodecInfo.GetImageDecoders()

        For Each codec As ImageCodecInfo In codecs
            If codec.FormatID = format.Guid Then
                Return codec
            End If
        Next

        Return Nothing
    End Function
#End Region

#Region "Map Rendering and Display"
    ''' <summary>
    ''' Generates the map bitmap based on the current render mode
    ''' </summary>
    Private Sub GenerateMapBitmap()
        If currentMap Is Nothing Then
            Return
        End If

        ' Calculate bitmap dimensions
        Dim width As Integer = CInt(currentMap.WidthInTiles()) * 32 ' 32 pixels per tile
        Dim height As Integer = CInt(currentMap.HeightInTiles()) * 32

        ' Create bitmap
        If mapBitmap IsNot Nothing Then
            mapBitmap.Dispose()
        End If
        mapBitmap = New Bitmap(width, height)

        ' Render the map based on current render mode
        Select Case currentRenderMode
            Case RenderMode.GameMap
                RenderGameMap()
            Case RenderMode.CellTypeOverlay
                RenderCellTypeOverlay()
            Case RenderMode.CellTypeOverlayAll
                RenderCellTypeOverlayAll()
            Case RenderMode.CellTypeBasic
                RenderCellTypeBasic()
            Case RenderMode.CellTypeAll
                RenderCellTypeAll()
        End Select
    End Sub

    ''' <summary>
    ''' Renders map in GameMap mode (actual tileset graphics)
    ''' </summary>
    Private Sub RenderGameMap()
        Using g As Graphics = Graphics.FromImage(mapBitmap)
            ' Draw background
            g.Clear(System.Drawing.Color.Black)

            ' Draw actual tileset images from the game files
            For y As Integer = 0 To currentMap.HeightInTiles() - 1
                For x As Integer = 0 To currentMap.WidthInTiles() - 1
                    ' Try to get the tile image
                    Dim tilesetIndex As Integer = currentMap.GetTilesetIndex(x, y)
                    Dim imageIndex As Integer = currentMap.GetImageIndex(x, y)

                    ' Check if we have valid indices
                    If tilesetIndex >= 0 AndAlso tilesetIndex < currentMap.tilesetSources.Count Then
                        Dim tilesetName As String = currentMap.tilesetSources(tilesetIndex).tilesetFilename

                        ' Check if we have this tileset loaded
                        If tilesetImages.ContainsKey(tilesetName) Then
                            Dim tileList As List(Of Bitmap) = tilesetImages(tilesetName)

                            ' Check if the image index is valid
                            If imageIndex >= 0 AndAlso imageIndex < tileList.Count Then
                                ' Draw the actual game tile image
                                g.DrawImage(tileList(imageIndex), x * 32, y * 32)
                                Continue For
                            End If
                        End If
                    End If

                    ' If we get here, we couldn't find the image - draw a fallback
                    Dim cellType As CellType = currentMap.GetCellType(x, y)
                    Dim tileColor As System.Drawing.Color = GetColorForCellType(cellType)
                    g.FillRectangle(New SolidBrush(tileColor), x * 32, y * 32, 32, 32)
                    g.DrawRectangle(Pens.DarkGray, x * 32, y * 32, 32, 32)
                Next
            Next
        End Using
    End Sub

    ''' <summary>
    ''' Renders map in CellTypeOverlay mode (game map with semi-transparent cell type overlay)
    ''' </summary>
    Private Sub RenderCellTypeOverlay()
        ' First render the game map as the base layer
        RenderGameMap()

        ' Now overlay the cell types with transparency
        Using g As Graphics = Graphics.FromImage(mapBitmap)
            ' Set up for transparency
            Dim colorMatrix As New Drawing.Imaging.ColorMatrix()
            colorMatrix.Matrix33 = 0.45F  ' Set alpha/transparency to 45%

            Dim imgAttributes As New Drawing.Imaging.ImageAttributes()
            imgAttributes.SetColorMatrix(colorMatrix, Drawing.Imaging.ColorMatrixFlag.Default, Drawing.Imaging.ColorAdjustType.Bitmap)

            ' Draw semi-transparent cell type colors over each tile
            For y As Integer = 0 To currentMap.HeightInTiles() - 1
                For x As Integer = 0 To currentMap.WidthInTiles() - 1
                    ' Get tile information
                    Dim cellType As CellType = currentMap.GetCellType(x, y)

                    ' Get a color that indicates passability clearly
                    Dim overlayColor As System.Drawing.Color = GetOverlayColorForCellType(cellType)

                    ' Create a single-use bitmap for the colored overlay
                    Using overlayBitmap As New Bitmap(32, 32)
                        Using overlayG As Graphics = Graphics.FromImage(overlayBitmap)
                            overlayG.FillRectangle(New SolidBrush(overlayColor), 0, 0, 32, 32)
                        End Using

                        ' Draw the colored overlay with transparency
                        g.DrawImage(overlayBitmap,
                                   New Rectangle(x * 32, y * 32, 32, 32),  ' Destination rectangle
                                   0, 0, 32, 32,                           ' Source rectangle
                                   GraphicsUnit.Pixel,
                                   imgAttributes)
                    End Using
                Next
            Next
        End Using
    End Sub

    ''' <summary>
    ''' Renders map in CellTypeOverlayAll mode (game map with cell type labels)
    ''' </summary>
    Private Sub RenderCellTypeOverlayAll()
        ' First render the game map as the base layer
        RenderGameMap()

        ' Now overlay cell type labels with colors based on cell type
        Using g As Graphics = Graphics.FromImage(mapBitmap)
            ' Draw text labels for each tile
            For y As Integer = 0 To currentMap.HeightInTiles() - 1
                For x As Integer = 0 To currentMap.WidthInTiles() - 1
                    ' Get tile information
                    Dim cellType As CellType = currentMap.GetCellType(x, y)

                    ' Get the abbreviation and color for this cell type
                    Dim labelInfo As Tuple(Of String, System.Drawing.Color) = GetCellTypeLabel(cellType)
                    Dim label As String = labelInfo.Item1
                    Dim textColor As System.Drawing.Color = labelInfo.Item2

                    ' Draw text label at tile center
                    Using font As New Font("Arial", 7, FontStyle.Bold)
                        Dim textSize As SizeF = g.MeasureString(label, font)
                        Dim xPos As Single = x * 32 + (32 - textSize.Width) / 2
                        Dim yPos As Single = y * 32 + (32 - textSize.Height) / 2

                        ' Add dark outline for better readability
                        ' Draw text shadow with slight offset in all directions
                        For offsetX As Integer = -1 To 1
                            For offsetY As Integer = -1 To 1
                                If Not (offsetX = 0 AndAlso offsetY = 0) Then ' Skip the center position
                                    g.DrawString(label, font, Brushes.Black, xPos + offsetX, yPos + offsetY)
                                End If
                            Next
                        Next

                        ' Draw actual text in the correct color
                        g.DrawString(label, font, New SolidBrush(textColor), xPos, yPos)
                    End Using
                Next
            Next
        End Using
    End Sub

    ''' <summary>
    ''' Renders map in CellTypeBasic mode (simplified cell type coloring)
    ''' </summary>
    Private Sub RenderCellTypeBasic()
        Using g As Graphics = Graphics.FromImage(mapBitmap)
            ' Draw background
            g.Clear(System.Drawing.Color.Black)

            ' Draw tiles with basic coloring
            For y As Integer = 0 To currentMap.HeightInTiles() - 1
                For x As Integer = 0 To currentMap.WidthInTiles() - 1
                    ' Get tile information
                    Dim cellType As CellType = currentMap.GetCellType(x, y)
                    Dim tileColor As System.Drawing.Color = GetColorForCellType(cellType)

                    ' Draw tile
                    g.FillRectangle(New SolidBrush(tileColor), x * 32, y * 32, 32, 32)
                    g.DrawRectangle(Pens.DarkGray, x * 32, y * 32, 32, 32)
                Next
            Next
        End Using
    End Sub

    ''' <summary>
    ''' Renders map in CellTypeAll mode (detailed cell type coloring with text)
    ''' </summary>
    Private Sub RenderCellTypeAll()
        Using g As Graphics = Graphics.FromImage(mapBitmap)
            ' Draw background
            g.Clear(System.Drawing.Color.Black)

            ' Draw tiles with detailed coloring
            For y As Integer = 0 To currentMap.HeightInTiles() - 1
                For x As Integer = 0 To currentMap.WidthInTiles() - 1
                    ' Get tile information
                    Dim cellType As CellType = currentMap.GetCellType(x, y)
                    Dim tileColor As System.Drawing.Color = GetDetailedColorForCellType(cellType)

                    ' Draw tile
                    g.FillRectangle(New SolidBrush(tileColor), x * 32, y * 32, 32, 32)
                    g.DrawRectangle(Pens.DarkGray, x * 32, y * 32, 32, 32)

                    ' Add cell type text for better visibility (optional)
                    Using font As New Font("Arial", 7)
                        Dim brush As New SolidBrush(System.Drawing.Color.White)
                        ' Only use cell type name's first 6 characters to fit within tile
                        Dim typeName As String = cellType.ToString()
                        If typeName.Length > 6 Then
                            typeName = typeName.Substring(0, 6)
                        End If
                        g.DrawString(typeName, font, brush, x * 32 + 2, y * 32 + 12)
                    End Using
                Next
            Next
        End Using
    End Sub

    ''' <summary>
    ''' Paints the map panel, drawing the map with appropriate zoom and grid
    ''' </summary>
    Private Sub MapPanel_Paint(sender As Object, e As PaintEventArgs)
        If mapBitmap Is Nothing Then
            Return
        End If

        ' Calculate scaled dimensions
        Dim scaledWidth As Integer = CInt(mapBitmap.Width * zoomLevel)
        Dim scaledHeight As Integer = CInt(mapBitmap.Height * zoomLevel)

        ' Draw map
        e.Graphics.InterpolationMode = Drawing2D.InterpolationMode.NearestNeighbor
        e.Graphics.PixelOffsetMode = Drawing2D.PixelOffsetMode.Half
        e.Graphics.DrawImage(mapBitmap, offsetX, offsetY, scaledWidth, scaledHeight)

        ' Draw grid if enabled
        If showGrid AndAlso currentMap IsNot Nothing Then
            ' Create a thin pen for grid lines
            Using gridPen As New Pen(System.Drawing.Color.Black, 1)
                ' Calculate tile size based on zoom level
                Dim tileSize As Integer = CInt(32 * zoomLevel)

                ' Draw vertical grid lines
                For x As Integer = 0 To currentMap.WidthInTiles()
                    Dim xPos As Integer = offsetX + (x * tileSize)
                    e.Graphics.DrawLine(gridPen, xPos, offsetY, xPos, offsetY + scaledHeight)
                Next

                ' Draw horizontal grid lines
                For y As Integer = 0 To currentMap.HeightInTiles()
                    Dim yPos As Integer = offsetY + (y * tileSize)
                    e.Graphics.DrawLine(gridPen, offsetX, yPos, offsetX + scaledWidth, yPos)
                Next
            End Using
        End If

        ' Draw selection 
        If currentTileX >= 0 AndAlso currentTileY >= 0 AndAlso
           currentTileX < currentMap.WidthInTiles() AndAlso currentTileY < currentMap.HeightInTiles() Then

            Dim selX As Integer = CInt(currentTileX * 32 * zoomLevel) + offsetX
            Dim selY As Integer = CInt(currentTileY * 32 * zoomLevel) + offsetY
            Dim selSize As Integer = CInt(32 * zoomLevel)

            e.Graphics.DrawRectangle(New Pen(System.Drawing.Color.White, 2), selX, selY, selSize, selSize)
        End If
    End Sub

    ''' <summary>
    ''' Updates a single tile in the map bitmap
    ''' </summary>
    ''' <param name="x">Tile X coordinate</param>
    ''' <param name="y">Tile Y coordinate</param>
    Private Sub UpdateSingleTile(x As Integer, y As Integer)
        If mapBitmap Is Nothing Then
            Return
        End If

        ' Update just the changed tile based on current render mode
        Using g As Graphics = Graphics.FromImage(mapBitmap)
            Select Case currentRenderMode
                Case RenderMode.GameMap
                    RenderSingleTileGameMap(g, x, y)
                Case RenderMode.CellTypeOverlay
                    RenderSingleTileGameMap(g, x, y)  ' Base layer
                    RenderSingleTileCellTypeOverlay(g, x, y)  ' Overlay
                Case RenderMode.CellTypeOverlayAll
                    RenderSingleTileGameMap(g, x, y)  ' Base layer
                    RenderSingleTileCellTypeOverlayAll(g, x, y)  ' Detailed overlay
                Case RenderMode.CellTypeBasic
                    RenderSingleTileCellTypeBasic(g, x, y)
                Case RenderMode.CellTypeAll
                    RenderSingleTileCellTypeAll(g, x, y)
            End Select
        End Using

        ' Only invalidate the region that changed
        Dim panel As Control = FindControl("pnlMap")
        If panel IsNot Nothing Then
            Dim tileX As Integer = CInt(x * 32 * zoomLevel) + offsetX
            Dim tileY As Integer = CInt(y * 32 * zoomLevel) + offsetY
            Dim tileSize As Integer = CInt(32 * zoomLevel) + 2  ' +2 for border
            panel.Invalidate(New Rectangle(tileX, tileY, tileSize, tileSize))
        End If
    End Sub

    ''' <summary>
    ''' Renders a single tile in GameMap mode
    ''' </summary>
    Private Sub RenderSingleTileGameMap(g As Graphics, x As Integer, y As Integer)
        ' Try to get the tile image
        Dim tilesetIndex As Integer = currentMap.GetTilesetIndex(x, y)
        Dim imageIndex As Integer = currentMap.GetImageIndex(x, y)

        ' Check if we have valid indices
        If tilesetIndex >= 0 AndAlso tilesetIndex < currentMap.tilesetSources.Count Then
            Dim tilesetName As String = currentMap.tilesetSources(tilesetIndex).tilesetFilename

            ' Check if we have this tileset loaded
            If tilesetImages.ContainsKey(tilesetName) Then
                Dim tileList As List(Of Bitmap) = tilesetImages(tilesetName)

                ' Check if the image index is valid
                If imageIndex >= 0 AndAlso imageIndex < tileList.Count Then
                    ' Draw the actual game tile image
                    g.DrawImage(tileList(imageIndex), x * 32, y * 32)
                    Return
                End If
            End If
        End If

        ' If we get here, we couldn't find the image - draw a fallback
        Dim cellType As CellType = currentMap.GetCellType(x, y)
        Dim tileColor As System.Drawing.Color = GetColorForCellType(cellType)
        g.FillRectangle(New SolidBrush(tileColor), x * 32, y * 32, 32, 32)
        g.DrawRectangle(Pens.DarkGray, x * 32, y * 32, 32, 32)
    End Sub

    ''' <summary>
    ''' Renders a single tile in CellTypeOverlay mode
    ''' </summary>
    Private Sub RenderSingleTileCellTypeOverlay(g As Graphics, x As Integer, y As Integer)
        ' Draw semi-transparent overlay
        Dim cellType As CellType = currentMap.GetCellType(x, y)
        Dim overlayColor As System.Drawing.Color = GetOverlayColorForCellType(cellType)

        ' Set up color matrix for transparency
        Dim colorMatrix As New Drawing.Imaging.ColorMatrix()
        colorMatrix.Matrix33 = 0.45F  ' Set alpha/transparency to 45%

        Dim imgAttributes As New Drawing.Imaging.ImageAttributes()
        imgAttributes.SetColorMatrix(colorMatrix, Drawing.Imaging.ColorMatrixFlag.Default, Drawing.Imaging.ColorAdjustType.Bitmap)

        ' Create overlay bitmap
        Using overlayBitmap As New Bitmap(32, 32)
            Using overlayG As Graphics = Graphics.FromImage(overlayBitmap)
                overlayG.FillRectangle(New SolidBrush(overlayColor), 0, 0, 32, 32)
            End Using

            ' Draw with transparency
            g.DrawImage(overlayBitmap,
                   New Rectangle(x * 32, y * 32, 32, 32),  ' Destination rectangle
                   0, 0, 32, 32,                           ' Source rectangle
                   GraphicsUnit.Pixel,
                   imgAttributes)
        End Using
    End Sub

    ''' <summary>
    ''' Renders a single tile in CellTypeOverlayAll mode
    ''' </summary>
    Private Sub RenderSingleTileCellTypeOverlayAll(g As Graphics, x As Integer, y As Integer)
        ' Get tile information
        Dim cellType As CellType = currentMap.GetCellType(x, y)

        ' Get the abbreviation and color for this cell type
        Dim labelInfo As Tuple(Of String, System.Drawing.Color) = GetCellTypeLabel(cellType)
        Dim label As String = labelInfo.Item1
        Dim textColor As System.Drawing.Color = labelInfo.Item2

        ' Draw text label at tile center
        Using font As New Font("Arial", 7, FontStyle.Bold)
            Dim textSize As SizeF = g.MeasureString(label, font)
            Dim xPos As Single = x * 32 + (32 - textSize.Width) / 2
            Dim yPos As Single = y * 32 + (32 - textSize.Height) / 2

            ' Add dark outline for better readability
            ' Draw text shadow with slight offset in all directions
            For offsetX As Integer = -1 To 1
                For offsetY As Integer = -1 To 1
                    If Not (offsetX = 0 AndAlso offsetY = 0) Then ' Skip the center position
                        g.DrawString(label, font, Brushes.Black, xPos + offsetX, yPos + offsetY)
                    End If
                Next
            Next

            ' Draw actual text in the correct color
            g.DrawString(label, font, New SolidBrush(textColor), xPos, yPos)
        End Using
    End Sub

    ''' <summary>
    ''' Renders a single tile in CellTypeBasic mode
    ''' </summary>
    Private Sub RenderSingleTileCellTypeBasic(g As Graphics, x As Integer, y As Integer)
        ' Get tile information
        Dim cellType As CellType = currentMap.GetCellType(x, y)
        Dim tileColor As System.Drawing.Color = GetColorForCellType(cellType)

        ' Draw tile
        g.FillRectangle(New SolidBrush(tileColor), x * 32, y * 32, 32, 32)
        g.DrawRectangle(Pens.DarkGray, x * 32, y * 32, 32, 32)
    End Sub

    ''' <summary>
    ''' Renders a single tile in CellTypeAll mode
    ''' </summary>
    Private Sub RenderSingleTileCellTypeAll(g As Graphics, x As Integer, y As Integer)
        ' Get tile information
        Dim cellType As CellType = currentMap.GetCellType(x, y)
        Dim tileColor As System.Drawing.Color = GetDetailedColorForCellType(cellType)

        ' Draw tile
        g.FillRectangle(New SolidBrush(tileColor), x * 32, y * 32, 32, 32)
        g.DrawRectangle(Pens.DarkGray, x * 32, y * 32, 32, 32)

        ' Add cell type text for better visibility
        Using font As New Font("Arial", 7)
            Dim brush As New SolidBrush(System.Drawing.Color.White)
            ' Only use cell type name's first 6 characters to fit within tile
            Dim typeName As String = cellType.ToString()
            If typeName.Length > 6 Then
                typeName = typeName.Substring(0, 6)
            End If
            g.DrawString(typeName, font, brush, x * 32 + 2, y * 32 + 12)
        End Using
    End Sub

    ''' <summary>
    ''' Updates the map tile display in the bitmap
    ''' </summary>
    Private Sub UpdateMapTile(x As Integer, y As Integer)
        If mapBitmap Is Nothing Then
            Return
        End If

        ' Update bitmap for the specific tile
        Using g As Graphics = Graphics.FromImage(mapBitmap)
            Dim cellType As CellType = currentMap.GetCellType(x, y)
            Dim tileColor As System.Drawing.Color = GetColorForCellType(cellType)

            g.FillRectangle(New SolidBrush(tileColor), x * 32, y * 32, 32, 32)
            g.DrawRectangle(Pens.DarkGray, x * 32, y * 32, 32, 32)
        End Using

        ' Refresh display
        FindControl("pnlMap").Invalidate()
    End Sub
#End Region

#Region "Mouse Event Handlers"
    ''' <summary>
    ''' Handles mouse movement over the map panel
    ''' </summary>
    Private Sub MapPanel_MouseMove(sender As Object, e As MouseEventArgs)
        If currentMap Is Nothing Then
            Return
        End If

        ' Update status bar with mouse coordinates
        Dim mapX As Integer = CInt((e.X - offsetX) / zoomLevel / 32)
        Dim mapY As Integer = CInt((e.Y - offsetY) / zoomLevel / 32)

        ' Check if coordinates are valid
        If mapX >= 0 AndAlso mapY >= 0 AndAlso mapX < currentMap.WidthInTiles() AndAlso mapY < currentMap.HeightInTiles() Then

            ' Ensure statusStrip is not null
            If statusStrip IsNot Nothing Then
                lblToolStripCoordinates.Text = "Coordinates: " & mapX + 1 & ", " & mapY + 1
            End If

            ' Handle dragging (panning)
            If isDragging Then
                offsetX += (e.X - lastMouseX)
                offsetY += (e.Y - lastMouseY)
                Dim mapPanel As Control = FindControl("pnlMap")
                If mapPanel IsNot Nothing Then
                    mapPanel.Invalidate()
                End If
            End If

            lastMouseX = e.X
            lastMouseY = e.Y
        End If
    End Sub

    ''' <summary>
    ''' Handles mouse down event on the map panel
    ''' </summary>
    Private Sub MapPanel_MouseDown(sender As Object, e As MouseEventArgs)
        If currentMap Is Nothing Then
            Return
        End If

        If e.Button = MouseButtons.Left Then
            ' Calculate tile position
            Dim mapX As Integer = CInt((e.X - offsetX) / zoomLevel / 32)
            Dim mapY As Integer = CInt((e.Y - offsetY) / zoomLevel / 32)

            ' Check if coordinates are valid
            If mapX >= 0 AndAlso mapY >= 0 AndAlso mapX < currentMap.WidthInTiles() AndAlso mapY < currentMap.HeightInTiles() Then
                ' Check if we're in edit mode
                If EditModeEnabled = True Then
                    ' Check if we need to switch to CellTypeOverlayAll mode for better editing visibility
                    If currentRenderMode <> RenderMode.CellTypeOverlayAll Then
                        ' Use the existing function to switch modes
                        RenderModeCellTypeOverlayAll_Click(Nothing, Nothing)
                    End If

                    ' Save previous cell type
                    Dim previousCellType As CellType = currentMap.GetCellType(mapX, mapY)

                    ' Only create a command if the cell type is actually changing
                    If previousCellType <> ActiveCellType Then
                        ' Create and execute the command
                        Dim command As New ChangeCellTypeCommand(currentMap, mapX, mapY, previousCellType, ActiveCellType)
                        ExecuteCommand(command)

                        ' Update the tile display (no need to regenerate the whole map)
                        UpdateSingleTile(mapX, mapY)

                        Debug.WriteLine("Cell Type Updated - " & ActiveCellType.ToString & " - " & mapX & "," & mapY)
                    End If
                End If

                ' Set as current tile for display in property panel
                currentTileX = mapX
                currentTileY = mapY

                ' Update property controls
                UpdatePropertyControls()

                ' Refresh selection display - we need to redraw the whole map to show the selection rectangle
                FindControl("pnlMap").Invalidate()
            End If
        ElseIf e.Button = MouseButtons.Right Then
            ' Start dragging for panning
            isDragging = True
            lastMouseX = e.X
            lastMouseY = e.Y

            ' Change cursor
            FindControl("pnlMap").Cursor = Cursors.Hand
        End If
    End Sub

    ''' <summary>
    ''' Handles mouse up event on the map panel
    ''' </summary>
    Private Sub MapPanel_MouseUp(sender As Object, e As MouseEventArgs)
        If e.Button = MouseButtons.Right Then
            ' Stop dragging
            isDragging = False

            ' Reset cursor
            FindControl("pnlMap").Cursor = Cursors.Default
        End If
    End Sub

    ''' <summary>
    ''' Handles mouse wheel event on the map panel for zooming
    ''' </summary>
    Private Sub MapPanel_MouseWheel(sender As Object, e As MouseEventArgs)
        ' Store old zoom level for positioning calculations
        Dim oldZoom As Single = zoomLevel

        ' Store mouse position relative to map before zoom
        Dim mouseMapX As Single = (e.X - offsetX) / oldZoom
        Dim mouseMapY As Single = (e.Y - offsetY) / oldZoom

        ' Fixed zoom levels (in decimal form)
        Dim zoomLevels As Single() = {0.1F, 0.25F, 0.5F, 0.75F, 1.0F, 1.25F, 1.5F}

        ' Find current index in the zoom levels array
        Dim currentIndex As Integer = -1
        For i As Integer = 0 To zoomLevels.Length - 1
            ' Use a small epsilon for floating point comparison
            If Math.Abs(zoomLevel - zoomLevels(i)) < 0.01F Then
                currentIndex = i
                Exit For
            End If
        Next

        ' If current zoom is not in our defined levels, find closest level
        If currentIndex = -1 Then
            Dim minDifference As Single = Single.MaxValue
            For i As Integer = 0 To zoomLevels.Length - 1
                Dim difference As Single = Math.Abs(zoomLevel - zoomLevels(i))
                If difference < minDifference Then
                    minDifference = difference
                    currentIndex = i
                End If
            Next
        End If

        ' Adjust index based on scroll direction
        Dim newIndex As Integer = currentIndex
        If e.Delta > 0 Then
            ' Zoom in - move to next higher zoom level
            newIndex = Math.Min(zoomLevels.Length - 1, currentIndex + 1)
        Else
            ' Zoom out - move to next lower zoom level
            newIndex = Math.Max(0, currentIndex - 1)
        End If

        ' Set the new zoom level
        zoomLevel = zoomLevels(newIndex)

        ' Adjust offset to zoom toward the mouse position
        offsetX = CInt(e.X - mouseMapX * zoomLevel)
        offsetY = CInt(e.Y - mouseMapY * zoomLevel)

        ' Update zoom level label - show as percentage
        lblZoomLevel.Text = $"Zoom Level: {Math.Round(zoomLevel * 100)}%"

        ' Refresh display
        FindControl("pnlMap").Invalidate()
    End Sub
#End Region

#Region "Tileset Loading and Management"
    ''' <summary>
    ''' Shows the tileset loading status message
    ''' </summary>
    Private Sub ShowTilesetLoadingStatus()
        ' Check if any tilesets were loaded successfully
        Dim loadedTilesets As Integer = 0
        Dim totalNeededTilesets As Integer = 0

        For Each kvp In tilesetImages
            If kvp.Value.Count > 0 Then
                loadedTilesets += 1
            End If
            totalNeededTilesets += 1
        Next

        If loadedTilesets = 0 And totalNeededTilesets > 0 Then
            ' No tilesets were loaded - inform the user
            Dim msg As String = "Could not load any tileset files. The map may not display correctly."
            MessageBox.Show(msg, "Tileset Loading Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End If
    End Sub

    ''' <summary>
    ''' Loads tilesets needed for the current map
    ''' </summary>
    Private Sub LoadMapTilesets()
        If currentMap Is Nothing Then
            Return
        End If

        ' Clear any previously loaded tilesets
        For Each bitmapList In tilesetImages.Values
            For Each bmp In bitmapList
                If bmp IsNot Nothing Then
                    bmp.Dispose()
                End If
            Next
        Next
        tilesetImages.Clear()

        ' Focus just on the expected path for Outpost 2 tilesets
        Dim tilesetPath As String = Path.Combine(My.Settings.OP2Path, "OPU", "base", "tilesets")

        Debug.WriteLine($"Loading tilesets from: {tilesetPath}")

        If Not Directory.Exists(tilesetPath) Then
            Debug.WriteLine($"ERROR: Tileset directory does not exist: {tilesetPath}")
            ' Try alternative paths
            tilesetPath = Path.Combine(My.Settings.WorkingPath, "base", "tilesets")
            If Not Directory.Exists(tilesetPath) Then
                tilesetPath = Path.Combine(My.Settings.WorkingPath, "tilesets")
                If Not Directory.Exists(tilesetPath) Then
                    tilesetPath = Path.Combine(My.Settings.WorkingPath, "OPU", "base", "tilesets")
                    If Not Directory.Exists(tilesetPath) Then
                        MessageBox.Show($"Could not find tileset directory in any of the expected locations.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                        Return
                    End If
                End If
            End If
        End If

        ' Load tilesets from the map
        For Each tilesetSource In currentMap.tilesetSources
            If Not tilesetSource.IsEmpty() Then
                Dim tilesetFilename As String = tilesetSource.tilesetFilename

                ' Ensure the filename has a .bmp extension
                If Not tilesetFilename.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) Then
                    tilesetFilename += ".bmp"
                End If

                'Debug.WriteLine($"Looking for tileset: {tilesetFilename}")

                Dim fullPath As String = Path.Combine(tilesetPath, tilesetFilename)

                If File.Exists(fullPath) Then
                    'Debug.WriteLine($"File exists, attempting to load")
                    Try
                        Using fileStream As New FileStream(fullPath, FileMode.Open, FileAccess.Read)
                            Dim tilesetBitmap As BitmapFile = TilesetLoader.ReadTileset(fileStream)
                            ExtractTilesFromTileset(tilesetSource.tilesetFilename, tilesetBitmap)
                        End Using
                    Catch ex As Exception
                        Debug.WriteLine($"ERROR loading tileset: {ex.Message}")
                        Debug.WriteLine(ex.StackTrace)
                        tilesetImages(tilesetSource.tilesetFilename) = New List(Of Bitmap)()
                    End Try
                Else
                    Debug.WriteLine($"File does not exist")
                    tilesetImages(tilesetSource.tilesetFilename) = New List(Of Bitmap)()
                End If
            End If
        Next

        ' Output summary of loaded tilesets
        'Debug.WriteLine("Tileset loading complete:")
        For Each kvp In tilesetImages
            'Debug.WriteLine($"- {kvp.Key}: {kvp.Value.Count} tiles loaded")
        Next

    End Sub

    ''' <summary>
    ''' Diagnostic function to check tileset loading paths
    ''' </summary>
    Private Sub DiagnoseTilesetLoading()
        ' Much reduced diagnostic function
        Debug.WriteLine("=========== TILESET LOADING DIAGNOSTICS ===========")

        ' Check tileset directory paths
        Dim tilesetPath As String = Path.Combine(My.Settings.WorkingPath, "OPU", "base", "tilesets")
        Debug.WriteLine($"Primary tileset path: {tilesetPath} - Exists: {Directory.Exists(tilesetPath)}")

        ' List alternate paths
        Dim alternatePaths = New String() {
            Path.Combine(My.Settings.WorkingPath, "base", "tilesets"),
            Path.Combine(My.Settings.WorkingPath, "tilesets")
        }

        For Each path In alternatePaths
            Debug.WriteLine($"Alternative path: {path} - Exists: {Directory.Exists(path)}")
        Next

        Debug.WriteLine("=========== END DIAGNOSTICS ===========")
    End Sub

    ''' <summary>
    ''' Loads a tileset from the specified file
    ''' </summary>
    ''' <param name="filePath">Path to the tileset file</param>
    ''' <param name="tilesetName">Name of the tileset</param>
    Private Sub LoadTilesetFromFile(filePath As String, tilesetName As String)
        Try
            Using fileStream As New FileStream(filePath, FileMode.Open, FileAccess.Read)
                Dim tilesetBitmap As BitmapFile = TilesetLoader.ReadTileset(fileStream)
                ExtractTilesFromTileset(tilesetName, tilesetBitmap)
            End Using
        Catch ex As Exception
            Debug.WriteLine($"Error loading tileset file {filePath}: {ex.Message}")
            ' Create a placeholder entry
            tilesetImages(tilesetName) = New List(Of Bitmap)
        End Try
    End Sub

    ''' <summary>
    ''' Extracts individual tiles from a tileset bitmap
    ''' </summary>
    ''' <param name="tilesetName">Name of the tileset</param>
    ''' <param name="tilesetBitmap">The tileset bitmap data</param>
    Private Sub ExtractTilesFromTileset(tilesetName As String, tilesetBitmap As BitmapFile)
        ' Get dimensions
        Dim tileWidth As Integer = 32
        Dim tileHeight As Integer = 32
        Dim totalHeight As Integer = Math.Abs(tilesetBitmap.imageHeader.height)
        Dim width As Integer = tilesetBitmap.imageHeader.width

        ' Verify the tileset has the expected width
        If width <> tileWidth Then
            Debug.WriteLine($"WARNING: Tileset {tilesetName} has unexpected width: {width} (expected {tileWidth})")
        End If

        ' Calculate number of tiles
        Dim totalTiles As Integer = totalHeight \ tileHeight
        'Debug.WriteLine($"Extracting tiles from {tilesetName}: {width}x{totalHeight} pixels, contains {totalTiles} tiles")

        ' Create a list to hold the individual tile bitmaps
        Dim tileBitmaps As New List(Of Bitmap)

        ' Convert the bitmap file to a System.Drawing.Bitmap
        Dim fullBitmap As Bitmap = ConvertToBitmap(tilesetBitmap)

        ' Extract each tile (stacked vertically)
        For i As Integer = 0 To totalTiles - 1
            ' Create a new bitmap for this tile
            Dim tileBitmap As New Bitmap(tileWidth, tileHeight)

            ' Calculate the Y position of this tile in the source bitmap
            Dim sourceY As Integer = i * tileHeight

            ' Copy the tile pixels to the new bitmap
            Using g As Graphics = Graphics.FromImage(tileBitmap)
                g.DrawImage(fullBitmap,
                        New Rectangle(0, 0, tileWidth, tileHeight),
                        New Rectangle(0, sourceY, tileWidth, tileHeight),
                        GraphicsUnit.Pixel)
            End Using

            ' Add to our collection
            tileBitmaps.Add(tileBitmap)
        Next

        ' Store in our dictionary
        tilesetImages(tilesetName) = tileBitmaps

        'Debug.WriteLine($"Successfully loaded {tilesetName}: {tileBitmaps.Count} tiles extracted")

        ' Dispose the full bitmap
        fullBitmap.Dispose()
    End Sub

    ''' <summary>
    ''' Converts a BitmapFile to System.Drawing.Bitmap
    ''' Including correctly handling OP2's BGR format and transparency
    ''' </summary>
    ''' <param name="bitmapFile">The bitmap file to convert</param>
    ''' <returns>A System.Drawing.Bitmap representation of the input</returns>
    Private Function ConvertToBitmap(bitmapFile As BitmapFile) As Bitmap
        Dim bitmap As New Bitmap(bitmapFile.imageHeader.width, Math.Abs(bitmapFile.imageHeader.height))

        ' Create a color palette for the bitmap
        ' Important: OP2 stores colors in BGR format, so we need to swap Red and Blue
        Dim palette(bitmapFile.palette.Length - 1) As System.Drawing.Color

        ' Check if index 0 should be treated as transparent
        Dim isIndex0Transparent As Boolean = False
        If bitmapFile.palette.Length > 0 Then
            ' In many OP2 tilesets, index 0 is white (255,255,255) which indicates transparency
            Dim p0 As OP2UtilityDotNet.Bitmap.Color = bitmapFile.palette(0)
            isIndex0Transparent = (p0.red > 250 AndAlso p0.green > 250 AndAlso p0.blue > 250)
            Debug.WriteLine($"Index 0 values: R={p0.red}, G={p0.green}, B={p0.blue}, isTransparent={isIndex0Transparent}")
        End If

        ' Build the palette with R/B swap for all OP2 tilesets
        For i As Integer = 0 To bitmapFile.palette.Length - 1
            ' Create color with Red and Blue swapped (BGR format to RGB)
            palette(i) = System.Drawing.Color.FromArgb(
                bitmapFile.palette(i).blue,   ' Red channel gets blue value
                bitmapFile.palette(i).green,  ' Green stays the same
                bitmapFile.palette(i).red)    ' Blue channel gets red value
        Next

        ' Extract pixels
        Dim height As Integer = Math.Abs(bitmapFile.imageHeader.height)
        Dim isTopDown As Boolean = bitmapFile.imageHeader.height < 0

        For y As Integer = 0 To height - 1
            For x As Integer = 0 To bitmapFile.imageHeader.width - 1
                ' Adjust y-coordinate based on bitmap orientation
                Dim adjustedY As Integer = If(isTopDown, y, height - 1 - y)

                Dim paletteIndex As Integer = bitmapFile.GetPixelPaletteIndex(x, adjustedY)
                If paletteIndex >= 0 AndAlso paletteIndex < palette.Length Then
                    ' Special handling for index 0 if it's transparent
                    If paletteIndex = 0 AndAlso isIndex0Transparent Then
                        ' Use light gray for transparent pixels in our display
                        bitmap.SetPixel(x, y, System.Drawing.Color.LightGray)
                    Else
                        ' Use the actual palette color
                        bitmap.SetPixel(x, y, palette(paletteIndex))
                    End If
                End If
            Next
        Next

        'Debug.WriteLine("Bitmap conversion complete with BGR color correction and transparency handling")
        Return bitmap
    End Function
#End Region

#Region "Property Panel Updates"
    ''' <summary>
    ''' Updates the property panel controls with information about the currently selected tile
    ''' </summary>
    Private Sub UpdatePropertyControls()
        If currentMap Is Nothing OrElse currentTileX < 0 OrElse currentTileY < 0 Then
            Return
        End If

        Try
            ' Get the tileset information for this tile
            Dim tilesetIndex As Integer = currentMap.GetTilesetIndex(currentTileX, currentTileY)
            Dim imageIndex As Integer = currentMap.GetImageIndex(currentTileX, currentTileY)
            Dim mappingIndex As UInteger = currentMap.GetTileMappingIndex(currentTileX, currentTileY)

            ' Get the well BMP filename
            Dim wellBmpFilename As String = "Unknown"
            If tilesetIndex >= 0 AndAlso tilesetIndex < currentMap.tilesetSources.Count Then
                wellBmpFilename = currentMap.tilesetSources(tilesetIndex).tilesetFilename
            End If

            ' Update the tileset labels
            lblTileset.Text = $"Tileset: {wellBmpFilename}"
            lblTilesetImage.Text = $"Image Index: {imageIndex}"
            lblMappingIndex.Text = $"Mapping Index: {mappingIndex}"

            ' Update coordinates and cell type labels
            'Get the current CellType
            Dim cellType As CellType = currentMap.GetCellType(currentTileX, currentTileY)

            lblCoordinates.Text = "Coordinates: " & currentTileX + 1 & ", " & currentTileY + 1
            lblCellType.Text = $"Cell Type: {cellType}"

            ' Update the tile preview images
            UpdateTilePreview(wellBmpFilename, imageIndex)

        Catch ex As Exception
            Debug.WriteLine($"Error in UpdatePropertyControls: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Updates the tile preview image with the current tile
    ''' </summary>
    ''' <param name="tilesetName">Name of the tileset</param>
    ''' <param name="imageIndex">Index of the image in the tileset</param>
    Private Sub UpdateTilePreview(tilesetName As String, imageIndex As Integer)
        ' Clear any previous images
        If selectedTileImage IsNot Nothing Then
            picTilePreview.Image = Nothing
            selectedTileImage.Dispose()
            selectedTileImage = Nothing
        End If

        picTileOriginal.Image = Nothing

        Debug.WriteLine($"Updating tile preview for tileset: {tilesetName}, index: {imageIndex}")

        ' Check if we have this tileset loaded
        If tilesetImages.ContainsKey(tilesetName) Then
            Dim tileList As List(Of Bitmap) = tilesetImages(tilesetName)

            ' Check if the image index is valid
            If imageIndex >= 0 AndAlso imageIndex < tileList.Count Then
                ' Create a copy of the tile bitmap to display in original size
                Dim originalTile As Bitmap = tileList(imageIndex)
                picTileOriginal.Image = originalTile

                ' Create an enlarged version for the preview
                selectedTileImage = New Bitmap(96, 96)  ' 3x larger for better viewing

                Using g As Graphics = Graphics.FromImage(selectedTileImage)
                    g.InterpolationMode = Drawing2D.InterpolationMode.NearestNeighbor
                    g.PixelOffsetMode = Drawing2D.PixelOffsetMode.Half
                    g.DrawImage(originalTile, New Rectangle(0, 0, 96, 96))
                End Using

                picTilePreview.Image = selectedTileImage
                Debug.WriteLine($"Displayed tile from {tilesetName} at index {imageIndex}")
                Return
            Else
                Debug.WriteLine($"Invalid image index {imageIndex} for tileset {tilesetName} (max: {tileList.Count - 1})")
            End If
        Else
            Debug.WriteLine($"Tileset {tilesetName} not found in loaded tilesets")
        End If

        ' If we get here, we couldn't find the image - display placeholders
        ' Original size placeholder
        Dim originalPlaceholder As New Bitmap(32, 32)
        Using g As Graphics = Graphics.FromImage(originalPlaceholder)
            g.Clear(System.Drawing.Color.LightGray)
            g.DrawRectangle(Pens.Red, 0, 0, 31, 31)
            g.DrawLine(Pens.Red, 0, 0, 31, 31)
            g.DrawLine(Pens.Red, 0, 31, 31, 0)
        End Using
        picTileOriginal.Image = originalPlaceholder

        ' Enlarged placeholder
        selectedTileImage = New Bitmap(96, 96)
        Using g As Graphics = Graphics.FromImage(selectedTileImage)
            g.Clear(System.Drawing.Color.LightGray)
            g.DrawRectangle(Pens.Red, 0, 0, 95, 95)
            g.DrawLine(Pens.Red, 0, 0, 95, 95)
            g.DrawLine(Pens.Red, 0, 95, 95, 0)

            ' Draw some text explaining the issue
            Using font As New Font("Arial", 8)
                g.DrawString($"Missing: {tilesetName}", font, Brushes.Black, 5, 5)
                g.DrawString($"Index: {imageIndex}", font, Brushes.Black, 5, 20)

                If tilesetImages.ContainsKey(tilesetName) Then
                    g.DrawString($"Max Index: {tilesetImages(tilesetName).Count - 1}", font, Brushes.Black, 5, 35)
                End If
            End Using
        End Using
        picTilePreview.Image = selectedTileImage
    End Sub
#End Region

#Region "Command Pattern and Undo/Redo Support"
    ''' <summary>
    ''' Executes a command and adds it to the undo stack
    ''' </summary>
    ''' <param name="command">The command to execute</param>
    Private Sub ExecuteCommand(command As Command)
        ' If we're in the middle of an undo/redo operation, don't add to the stack
        If isUndoRedo Then
            command.Execute()
            Return
        End If

        ' Execute the command
        command.Execute()

        ' Add to undo stack
        undoStack.Push(command)

        ' Clear redo stack when a new command is executed
        redoStack.Clear()

        Debug.WriteLine($"Command executed: {command.Description}")
    End Sub
#End Region

#Region "Helper Functions"
    ''' <summary>
    ''' Gets a human-readable label for a cell type
    ''' </summary>
    ''' <param name="cellType">The cell type</param>
    ''' <returns>A tuple with label text and color</returns>
    Private Function GetCellTypeLabel(cellType As CellType) As Tuple(Of String, System.Drawing.Color)
        ' Returns a tuple with the abbreviated label and color for each cell type
        Select Case cellType
            ' Fast Passable - Bright Green
            Case CellType.FastPassible1
                Return New Tuple(Of String, System.Drawing.Color)("Fast1", System.Drawing.Color.LawnGreen)
            Case CellType.FastPassible2
                Return New Tuple(Of String, System.Drawing.Color)("Fast2", System.Drawing.Color.LawnGreen)

            ' Medium Passable - Yellow
            Case CellType.MediumPassible1
                Return New Tuple(Of String, System.Drawing.Color)("Med1", System.Drawing.Color.Yellow)
            Case CellType.MediumPassible2
                Return New Tuple(Of String, System.Drawing.Color)("Med2", System.Drawing.Color.Yellow)

            ' Slow Passable - Orange
            Case CellType.SlowPassible1
                Return New Tuple(Of String, System.Drawing.Color)("Slow1", System.Drawing.Color.Orange)
            Case CellType.SlowPassible2
                Return New Tuple(Of String, System.Drawing.Color)("Slow2", System.Drawing.Color.Orange)

            ' Impassable - Red
            Case CellType.Impassible1
                Return New Tuple(Of String, System.Drawing.Color)("Imp1", System.Drawing.Color.Red)
            Case CellType.Impassible2
                Return New Tuple(Of String, System.Drawing.Color)("Imp2", System.Drawing.Color.Red)

            ' Cliffs - Red
            Case CellType.NorthCliffs
                Return New Tuple(Of String, System.Drawing.Color)("N Clif", System.Drawing.Color.Red)
            Case CellType.CliffsHighSide
                Return New Tuple(Of String, System.Drawing.Color)("Clif H", System.Drawing.Color.Red)
            Case CellType.CliffsLowSide
                Return New Tuple(Of String, System.Drawing.Color)("Clif L", System.Drawing.Color.Red)

            ' Vents - White
            Case CellType.VentsAndFumaroles
                Return New Tuple(Of String, System.Drawing.Color)("Vent", System.Drawing.Color.White)

            ' Everything else - default labels in white
            Case CellType.DozedArea
                Return New Tuple(Of String, System.Drawing.Color)("Dozed", System.Drawing.Color.LightGreen)
            Case CellType.Rubble
                Return New Tuple(Of String, System.Drawing.Color)("Rubble", System.Drawing.Color.Red)
            Case CellType.NormalWall
                Return New Tuple(Of String, System.Drawing.Color)("Wall", System.Drawing.Color.Red)
            Case CellType.MicrobeWall
                Return New Tuple(Of String, System.Drawing.Color)("MWall", System.Drawing.Color.Red)
            Case CellType.LavaWall
                Return New Tuple(Of String, System.Drawing.Color)("LWall", System.Drawing.Color.Red)
            Case CellType.Tube0
                Return New Tuple(Of String, System.Drawing.Color)("Tube0", System.Drawing.Color.Cyan)
            Case CellType.Tube1
                Return New Tuple(Of String, System.Drawing.Color)("Tube1", System.Drawing.Color.Cyan)
            Case CellType.Tube2
                Return New Tuple(Of String, System.Drawing.Color)("Tube2", System.Drawing.Color.Cyan)
            Case CellType.Tube3
                Return New Tuple(Of String, System.Drawing.Color)("Tube3", System.Drawing.Color.Cyan)
            Case CellType.Tube4
                Return New Tuple(Of String, System.Drawing.Color)("Tube4", System.Drawing.Color.Cyan)
            Case CellType.Tube5
                Return New Tuple(Of String, System.Drawing.Color)("Tube5", System.Drawing.Color.Cyan)
            Case Else
                Return New Tuple(Of String, System.Drawing.Color)(cellType.ToString().Substring(0, Math.Min(5, cellType.ToString().Length)), System.Drawing.Color.White)
        End Select
    End Function

    ''' <summary>
    ''' Gets a color for a cell type in basic view
    ''' </summary>
    ''' <param name="cellType">The cell type</param>
    ''' <returns>A color representing the cell type</returns>
    Private Function GetColorForCellType(cellType As CellType) As System.Drawing.Color
        ' Return different colors based on cell type
        Select Case cellType
            Case CellType.FastPassible1, CellType.FastPassible2
                Return System.Drawing.Color.LightGreen
            Case CellType.SlowPassible1, CellType.SlowPassible2
                Return System.Drawing.Color.Yellow
            Case CellType.MediumPassible1, CellType.MediumPassible2
                Return System.Drawing.Color.Olive
            Case CellType.Impassible1, CellType.Impassible2
                Return System.Drawing.Color.DarkRed
            Case CellType.CliffsHighSide, CellType.CliffsLowSide, CellType.NorthCliffs
                Return System.Drawing.Color.Sienna
            Case CellType.VentsAndFumaroles
                Return System.Drawing.Color.Orange
            Case CellType.DozedArea
                Return System.Drawing.Color.LightGray
            Case CellType.Rubble
                Return System.Drawing.Color.DarkGray
            Case CellType.NormalWall
                Return System.Drawing.Color.Gray
            Case CellType.MicrobeWall
                Return System.Drawing.Color.Purple
            Case CellType.LavaWall
                Return System.Drawing.Color.OrangeRed
            Case CellType.Tube0, CellType.Tube1, CellType.Tube2, CellType.Tube3, CellType.Tube4, CellType.Tube5
                Return System.Drawing.Color.Cyan
            Case Else
                Return System.Drawing.Color.White
        End Select
    End Function

    ''' <summary>
    ''' Gets a detailed color for a cell type
    ''' </summary>
    ''' <param name="cellType">The cell type</param>
    ''' <returns>A color representing the cell type with more detail</returns>
    Private Function GetDetailedColorForCellType(cellType As CellType) As System.Drawing.Color
        ' Return more detailed colors based on passability
        Select Case cellType
            ' Fast Passable - Bright Green
            Case CellType.FastPassible1, CellType.FastPassible2
                Return System.Drawing.Color.FromArgb(0, 255, 0)

            ' Medium Passable - Yellow
            Case CellType.MediumPassible1, CellType.MediumPassible2
                Return System.Drawing.Color.FromArgb(255, 255, 0)

            ' Slow Passable - Orange
            Case CellType.SlowPassible1, CellType.SlowPassible2
                Return System.Drawing.Color.FromArgb(255, 128, 0)

            ' Impassable - Red
            Case CellType.Impassible1, CellType.Impassible2
                Return System.Drawing.Color.FromArgb(255, 0, 0)

            ' Cliffs - Brown
            Case CellType.NorthCliffs, CellType.CliffsHighSide, CellType.CliffsLowSide
                Return System.Drawing.Color.FromArgb(139, 69, 19)

            ' Vents - Purple
            Case CellType.VentsAndFumaroles
                Return System.Drawing.Color.FromArgb(128, 0, 128)

            ' Dozed Areas - Light Gray
            Case CellType.DozedArea
                Return System.Drawing.Color.FromArgb(192, 192, 192)

            ' Rubble - Dark Gray
            Case CellType.Rubble
                Return System.Drawing.Color.FromArgb(64, 64, 64)

            ' Walls - Different shades of gray
            Case CellType.NormalWall
                Return System.Drawing.Color.FromArgb(128, 128, 128)
            Case CellType.MicrobeWall
                Return System.Drawing.Color.FromArgb(128, 0, 255)
            Case CellType.LavaWall
                Return System.Drawing.Color.FromArgb(255, 69, 0)

            ' Tubes - Cyan
            Case CellType.Tube0, CellType.Tube1, CellType.Tube2, CellType.Tube3, CellType.Tube4, CellType.Tube5
                Return System.Drawing.Color.FromArgb(0, 255, 255)

                ' Default/Unknown
            Case Else
                Return System.Drawing.Color.White
        End Select
    End Function

    ''' <summary>
    ''' Gets a color for cell type overlay visualization
    ''' </summary>
    ''' <param name="cellType">The cell type</param>
    ''' <returns>A color suitable for overlay display</returns>
    Private Function GetOverlayColorForCellType(cellType As CellType) As System.Drawing.Color
        ' Return colors for overlay visualization (like in OP2MissionEditor)
        Select Case cellType
            ' Impassable - Red
            Case CellType.Impassible1, CellType.Impassible2,
                 CellType.NorthCliffs, CellType.CliffsHighSide, CellType.CliffsLowSide,
                 CellType.Rubble, CellType.NormalWall, CellType.MicrobeWall, CellType.LavaWall
                Return System.Drawing.Color.FromArgb(192, 0, 0)  ' Deep Red

            ' Slow Passable - Orange
            Case CellType.SlowPassible1, CellType.SlowPassible2, CellType.VentsAndFumaroles
                Return System.Drawing.Color.FromArgb(255, 128, 0)  ' Orange

            ' Medium Passable - Yellow
            Case CellType.MediumPassible1, CellType.MediumPassible2
                Return System.Drawing.Color.FromArgb(255, 255, 0)  ' Yellow

            ' Fast Passable - Green
            Case CellType.FastPassible1, CellType.FastPassible2, CellType.DozedArea
                Return System.Drawing.Color.FromArgb(0, 255, 0)  ' Bright Green

            ' Tubes - Cyan
            Case CellType.Tube0, CellType.Tube1, CellType.Tube2, CellType.Tube3, CellType.Tube4, CellType.Tube5
                Return System.Drawing.Color.FromArgb(0, 255, 255)  ' Cyan

                ' Default - White
            Case Else
                Return System.Drawing.Color.White
        End Select
    End Function

    ''' <summary>
    ''' Helper method to find a control by name
    ''' </summary>
    ''' <param name="controlName">Name of the control to find</param>
    ''' <returns>The found control or Nothing</returns>
    Private Function FindControl(controlName As String) As Control
        Dim foundControls As Control() = Controls.Find(controlName, True)
        If foundControls IsNot Nothing AndAlso foundControls.Length > 0 Then
            Return foundControls(0)
        End If
        Return Nothing
    End Function
#End Region

#Region "Tile Groups"

    ''' <summary>
    ''' Checks if the map has corrupted tile group names by examining only the first tile group
    ''' </summary>
    ''' <returns>True if corruption is detected, False if names appear valid</returns>
    Private Function HasCorruptTileGroupNames() As Boolean
        ' Make sure we have at least one tile group
        If currentMap Is Nothing OrElse currentMap.tileGroups Is Nothing OrElse currentMap.tileGroups.Count = 0 Then
            Return False
        End If

        ' Get the first tile group
        Dim group As TileGroup = currentMap.tileGroups(0)

        ' Check if name is null or empty
        If group Is Nothing OrElse String.IsNullOrEmpty(group.name) Then
            Return False
        End If

        ' Check for control characters (most common corruption)
        For i As Integer = 0 To group.name.Length - 1
            Dim c As Char = group.name(i)

            ' If we find ANY control characters, the name is corrupt
            If AscW(c) < 32 Then
                Return True
            End If
        Next

        Return False
    End Function

#End Region

#Region "Drag Drop"
    ''' <summary>
    ''' Handles drag enter event to allow file dropping onto the map panel
    ''' </summary>
    Private Sub pnlMap_DragEnter(sender As Object, e As DragEventArgs) Handles pnlMap.DragEnter
        If e.Data.GetDataPresent(DataFormats.FileDrop) Then
            e.Effect = DragDropEffects.Copy
        Else
            e.Effect = DragDropEffects.None
        End If
    End Sub

    ''' <summary>
    ''' Processes files dropped onto the map panel and attempts to load them
    ''' </summary>
    Private Sub pnlMap_DragDrop(sender As Object, e As DragEventArgs) Handles pnlMap.DragDrop
        Dim files() As String = CType(e.Data.GetData(DataFormats.FileDrop), String())
        For Each file As String In files
            If IO.File.Exists(file) Then
                'MessageBox.Show("Lets load the draged file: " & file & vbCrLf)
                Debug.WriteLine("File draged onto form: " & file)
                ' Try to load the map file
                If LoadMapFile(file) Then
                    Debug.WriteLine(" - Map loaded successfully")
                    MessageBox.Show("Map loaded successfully.", "Map Loaded", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Else
                    Debug.WriteLine(" - Failed to load map")
                End If
            End If
        Next
    End Sub
#End Region


End Class
