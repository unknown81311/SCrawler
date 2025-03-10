' Copyright (C) 2022  Andy
' This program is free software: you can redistribute it and/or modify
' it under the terms of the GNU General Public License as published by
' the Free Software Foundation, either version 3 of the License, or
' (at your option) any later version.
'
' This program is distributed in the hope that it will be useful,
' but WITHOUT ANY WARRANTY
Imports System.ComponentModel
Imports System.Globalization
Imports System.Threading
Imports PersonalUtilities.Forms
Imports SCrawler.API
Imports SCrawler.API.Base
Imports SCrawler.Editors
Imports SCrawler.DownloadObjects
Imports SCrawler.Plugin.Hosts
Public Class MainFrame
    Private MyView As FormsView
    Private ReadOnly _VideoDownloadingMode As Boolean = False
    Private MyChannels As ChannelViewForm
    Private MySavedPosts As DownloadSavedPostsForm
    Private _UFinit As Boolean = True
    Public Sub New()
        InitializeComponent()
        Dim n As DateTimeFormatInfo = CultureInfo.GetCultureInfo("en-us").DateTimeFormat.Clone
        n.FullDateTimePattern = "ddd MMM dd HH:mm:ss +ffff yyyy"
        n.TimeSeparator = String.Empty
        Twitter.DateProvider = New ADateTime(DirectCast(n.Clone, DateTimeFormatInfo)) With {.DateTimeStyle = DateTimeStyles.AssumeUniversal}
        Settings = New SettingsCLS
        With Settings.Plugins
            If .Count > 0 Then
                For i% = 0 To .Count - 1
                    MENU_SETTINGS.DropDownItems.Insert(MENU_SETTINGS.DropDownItems.Count - 2, .Item(i).Settings.GetSettingsButton)
                Next
            End If
        End With
        Dim Args() As String = Environment.GetCommandLineArgs
        If Args.ListExists(2) AndAlso Args(1) = "v" Then
            Using f As New VideosDownloaderForm : f.ShowDialog() : End Using
            _VideoDownloadingMode = True
        End If
    End Sub
    Private Sub MainFrame_Load(sender As Object, e As EventArgs) Handles Me.Load
        If _VideoDownloadingMode Then GoTo FormClosingInvoker
        Settings.DeleteCachePath()
        MainFrameObj = New MainFrameObjects(Me)
        MainProgress = New Toolbars.MyProgress(Toolbar_BOTTOM, PR_MAIN, LBL_STATUS, "Downloading profiles' data") With {
            .DropCurrentProgressOnTotalChange = False, .Enabled = False}
        Downloader = New TDownloader
        InfoForm = New DownloadedInfoForm
        MyProgressForm = New ActiveDownloadingProgress
        Downloader.ReconfPool()
        AddHandler Downloader.OnJobsChange, AddressOf Downloader_UpdateJobsCount
        AddHandler Downloader.OnDownloading, AddressOf Downloader_OnDownloading
        AddHandler Downloader.OnDownloadCountChange, AddressOf InfoForm.Downloader_OnDownloadCountChange
        AddHandler Downloader.SendNotification, AddressOf NotificationMessage
        AddHandler InfoForm.OnUserLooking, AddressOf Info_OnUserLooking
        Settings.LoadUsers()
        MyView = New FormsView(Me)
        MyView.ImportFromXML(Settings.Design)
        MyView.SetMeSize()
        If Settings.CloseToTray Then TrayIcon.Visible = True
        With LIST_PROFILES.Groups
            .AddRange(GetLviGroupName(Nothing, True, False)) 'collections
            .AddRange(GetLviGroupName(Nothing, False, True)) 'channels
            If Settings.Plugins.Count > 0 Then
                For Each h As SettingsHost In Settings.Plugins.Select(Function(hh) hh.Settings) : .AddRange(GetLviGroupName(h, False, False)) : Next
            End If
            If Settings.Labels.Count > 0 Then Settings.Labels.ToList.ForEach(Sub(l) .Add(New ListViewGroup(l, l)))
            .Add(Settings.Labels.NoLabel)
        End With
        With Settings
            LIST_PROFILES.View = .ViewMode
            LIST_PROFILES.ShowGroups = .UseGrouping
            ApplyViewPattern(.ViewMode.Value)
            AddHandler .Labels.NewLabelAdded, AddressOf UpdateLabelsGroups
        End With
        UserListLoader = New ListImagesLoader(LIST_PROFILES)
        RefillList()
        UpdateLabelsGroups()
        SetShowButtonsCheckers(Settings.ShowingMode.Value)
        CheckVersion(False)
        BTT_SITE_ALL.Checked = Settings.SelectedSites.Count = 0
        BTT_SITE_SPECIFIC.Checked = Settings.SelectedSites.Count > 0
        BTT_SHOW_LIMIT_DATES.Checked = Settings.LastUpdatedDate.HasValue
        _UFinit = False
        GoTo EndFunction
FormClosingInvoker:
        Close()
EndFunction:
    End Sub
    Private _CloseInvoked As Boolean = False
    Private _IgnoreTrayOptions As Boolean = False
    Private _IgnoreCloseConfirm As Boolean = False
    Private Async Sub MainFrame_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing
        If Settings.CloseToTray And Not _IgnoreTrayOptions Then
            e.Cancel = True
            Hide()
        Else
            If Not _VideoDownloadingMode Then
                If CheckForClose(_IgnoreCloseConfirm) Then
                    If _CloseInvoked Then GoTo CloseResume
                    Dim ChannelsWorking As Func(Of Boolean) = Function() If(MyChannels?.Working, False)
                    Dim SP_Working As Func(Of Boolean) = Function() If(MySavedPosts?.Working, False)
                    If (Not Downloader.Working And Not ChannelsWorking.Invoke And Not SP_Working.Invoke) OrElse
                        MsgBoxE({"Program still downloading something..." & vbNewLine &
                                 "Do you really want to stop downloading and exit of program?",
                                 "Downloading in progress"},
                                MsgBoxStyle.Exclamation,,,
                                {"Stop downloading and close", "Cancel"}) = 0 Then
                        If Downloader.Working Then _CloseInvoked = True : Downloader.Stop()
                        If ChannelsWorking.Invoke Then _CloseInvoked = True : MyChannels.Stop(False)
                        If SP_Working.Invoke Then _CloseInvoked = True : MySavedPosts.Stop()
                        If _CloseInvoked Then
                            e.Cancel = True
                            Await Task.Run(Sub()
                                               While Downloader.Working Or ChannelsWorking.Invoke Or SP_Working.Invoke : Thread.Sleep(500) : End While
                                           End Sub)
                        End If
                        Downloader.Dispose()
                        InfoForm.Dispose()
                        If Not MyChannels Is Nothing Then MyChannels.Dispose()
                        If Not VideoDownloader Is Nothing Then VideoDownloader.Dispose()
                        If Not MySavedPosts Is Nothing Then MySavedPosts.Dispose()
                        MyView.Dispose(Settings.Design)
                        Settings.Dispose()
                    Else
                        GoTo DropCloseParams
                    End If
                Else
                    GoTo DropCloseParams
                End If
            End If
            GoTo CloseContinue
DropCloseParams:
            e.Cancel = True
            _IgnoreTrayOptions = False
            _IgnoreCloseConfirm = False
            _CloseInvoked = False
            Exit Sub
CloseContinue:
            If Not BATCH Is Nothing Then BATCH.Dispose() : BATCH = Nothing
            If Not MyMainLOG.IsEmptyString Then SaveLogToFile()
            If _CloseInvoked Then Close()
CloseResume:
        End If
    End Sub
#Region "Tray"
    Private Sub TrayIcon_MouseClick(sender As Object, e As MouseEventArgs) Handles TrayIcon.MouseClick
        If e.Button = MouseButtons.Left Then
            If Visible Then Hide() Else Show()
        End If
    End Sub
    Private Sub BTT_TRAY_SHOW_HIDE_Click(sender As Object, e As EventArgs) Handles BTT_TRAY_SHOW_HIDE.Click
        If Visible Then Hide() Else Show()
    End Sub
    Private Sub BTT_TRAY_CLOSE_Click(sender As Object, e As EventArgs) Handles BTT_TRAY_CLOSE.Click
        If CheckForClose(False) Then _IgnoreTrayOptions = True : _IgnoreCloseConfirm = True : Close()
    End Sub
    Private Function CheckForClose(ByVal _Ignore As Boolean) As Boolean
        If Settings.ExitConfirm And Not _Ignore Then
            Return MsgBoxE({"Do you want to close the program?", "Closing the program"}, MsgBoxStyle.YesNo) = MsgBoxResult.Yes
        Else
            Return True
        End If
    End Function
#End Region
    Private Sub MainFrame_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        Dim b As Boolean = True
        Select Case e.KeyCode
            Case Keys.Insert : BTT_ADD_USER.PerformClick()
            Case Keys.Delete : DeleteSelectedUser()
            Case Keys.Enter : OpenFolder()
            Case Keys.F1 : BTT_VERSION_INFO.PerformClick()
            Case Keys.F2 : DownloadVideoByURL()
            Case Keys.F3 : EditSelectedUser()
            Case Keys.F5 : BTT_DOWN_SELECTED.PerformClick()
            Case Keys.F6 : If Settings.ShowingMode.Value = ShowingModes.All Then BTT_DOWN_ALL.PerformClick()
            Case Else : b = False
        End Select
        If b Then e.Handled = True
    End Sub
    Private Sub BTT_VERSION_INFO_Click(sender As Object, e As EventArgs) Handles BTT_VERSION_INFO.Click
        CheckVersion(True)
    End Sub
    Friend Sub RefillList()
        UserListLoader.Update()
        GC.Collect()
    End Sub
    Private Sub UserListUpdate(ByVal User As IUserData, ByVal Add As Boolean)
        UserListLoader.UpdateUser(User, Add)
    End Sub
    Private Sub UpdateLabelsGroups()
        If Settings.Labels.NewLabelsExists Then
            If Settings.Labels.NewLabels.Count > 0 Then
                Dim ll As ListViewGroup = Nothing
                Dim a As Action = Sub() LIST_PROFILES.Groups.Add(ll)
                For Each l$ In Settings.Labels.NewLabels
                    ll = New ListViewGroup(l, l)
                    If Not LIST_PROFILES.Groups.Contains(ll) Then
                        If LIST_PROFILES.InvokeRequired Then LIST_PROFILES.Invoke(a) Else a.Invoke
                    End If
                Next
            End If
            Settings.Labels.NewLabels.Clear()
        End If
    End Sub
    Private Sub OnUsersAddedHandler(ByVal StartIndex As Integer)
        If StartIndex <= Settings.Users.Count - 1 Then
            For i% = StartIndex To Settings.Users.Count - 1 : UserListUpdate(Settings.Users(i), True) : Next
        End If
    End Sub
#Region "Toolbar buttons"
#Region "Settings"
    Private Sub BTT_SETTINGS_Click(sender As Object, e As EventArgs) Handles BTT_SETTINGS.Click
        With Settings
            Dim mhl% = .MaxLargeImageHeigh.Value
            Dim mhs% = .MaxSmallImageHeigh.Value
            Dim sg As Boolean = .ShowGroups
            Using f As New GlobalSettingsForm
                f.ShowDialog()
                If f.DialogResult = DialogResult.OK Then
                    If ((Not .MaxLargeImageHeigh = mhl Or Not .MaxSmallImageHeigh = mhs) And .ViewModeIsPicture) Or
                        (Not sg = Settings.ShowGroups And .UseGrouping) Then RefillList()
                    TrayIcon.Visible = .CloseToTray
                    LIST_PROFILES.ShowGroups = .UseGrouping
                End If
            End Using
        End With
    End Sub
#End Region
#Region "User"
    Private Sub BTT_ADD_USER_Click(sender As Object, e As EventArgs) Handles BTT_ADD_USER.Click
        Using f As New UserCreatorForm
            f.ShowDialog()
            If f.DialogResult = DialogResult.OK Or f.StartIndex >= 0 Then
                Dim i%
                If f.StartIndex >= 0 Then
                    OnUsersAddedHandler(f.StartIndex)
                Else
                    Dim SimpleUser As Predicate(Of IUserData) = Function(u) u.Site = f.User.Site And u.Name = f.User.Name
                    i = Settings.Users.FindIndex(Function(u) If(u.IsCollection, DirectCast(u, UserDataBind).Collections.Exists(SimpleUser), SimpleUser.Invoke(u)))
                    If i < 0 Then
                        If Not UserBanned(f.User.Name) Then
                            Settings.UpdateUsersList(f.User)
                            Settings.Users.Add(UserDataBase.GetInstance(f.User))
                            With Settings.Users.Last
                                If Not .FileExists Then
                                    .Favorite = f.UserFavorite
                                    .Temporary = f.UserTemporary
                                    .ParseUserMediaOnly = f.UserMediaOnly
                                    .ReadyForDownload = f.UserReady
                                    .DownloadImages = f.DownloadImages
                                    .DownloadVideos = f.DownloadVideos
                                    .FriendlyName = f.UserFriendly
                                    .Description = f.UserDescr
                                    .ScriptUse = f.ScriptUse
                                    .ScriptData = f.ScriptData
                                    If Not f.MyExchangeOptions Is Nothing Then DirectCast(.Self, UserDataBase).ExchangeOptionsSet(f.MyExchangeOptions)
                                    Settings.Labels.Add(LabelsKeeper.NoParsedUser)
                                    .Self.Labels.ListAddList(f.UserLabels.ListAddValue(LabelsKeeper.NoParsedUser), LAP.ClearBeforeAdd, LAP.NotContainsOnly)
                                    .UpdateUserInformation()
                                End If
                            End With
                            UserListUpdate(Settings.Users.Last, True)
                            FocusUser(Settings.Users(Settings.Users.Count - 1).Key)
                        Else
                            MsgBoxE($"User [{f.User.Name}] was not added")
                        End If
                    Else
                        FocusUser(Settings.Users(i).Key)
                        MsgBoxE($"User [{f.User.Name}] already exists", MsgBoxStyle.Exclamation)
                    End If
                End If
            End If
        End Using
    End Sub
    Private Sub BTT_EDIT_USER_Click(sender As Object, e As EventArgs) Handles BTT_EDIT_USER.Click
        EditSelectedUser()
    End Sub
    Private Sub BTT_DELETE_USER_Click(sender As Object, e As EventArgs) Handles BTT_DELETE_USER.Click
        DeleteSelectedUser()
    End Sub
    Private Sub BTT_REFRESH_Click(sender As Object, e As EventArgs) Handles BTT_REFRESH.Click
        RefillList()
    End Sub
    Private Sub BTT_SHOW_INFO_Click(sender As Object, e As EventArgs) Handles BTT_SHOW_INFO.Click
        ShowInfoForm(True)
    End Sub
    Private Overloads Sub ShowInfoForm()
        ShowInfoForm(False)
    End Sub
    Private Overloads Sub ShowInfoForm(ByVal BringToFrontIfOpen As Boolean)
        If InfoForm.Visible Then
            If BringToFrontIfOpen Then InfoForm.BringToFront()
        Else
            InfoForm.Show()
        End If
    End Sub
    Private Sub BTT_CHANNELS_Click(sender As Object, e As EventArgs) Handles BTT_CHANNELS.Click
        If MyChannels Is Nothing Then
            MyChannels = New ChannelViewForm
            AddHandler MyChannels.OnUsersAdded, AddressOf OnUsersAddedHandler
            AddHandler MyChannels.OnDownloadDone, AddressOf NotificationMessage
        End If
        If MyChannels.Visible Then MyChannels.BringToFront() Else MyChannels.Show()
    End Sub
    Private Sub BTT_DOWN_SAVED_Click(sender As Object, e As EventArgs) Handles BTT_DOWN_SAVED.Click
        If MySavedPosts Is Nothing Then
            MySavedPosts = New DownloadSavedPostsForm
            AddHandler MySavedPosts.OnDownloadDone, AddressOf NotificationMessage
        End If
        With MySavedPosts
            If .Visible Then .BringToFront() Else .Show()
        End With
    End Sub
#End Region
#Region "Download"
    Private Sub BTT_DOWN_SELECTED_Click(sender As Object, e As EventArgs) Handles BTT_DOWN_SELECTED.Click
        DownloadSelectedUser(DownUserLimits.None)
    End Sub
#Region "Download all"
    Private Sub BTT_DOWN_ALL_Click(sender As Object, e As EventArgs) Handles BTT_DOWN_ALL.Click
        Downloader.AddRange(Settings.Users.Where(Function(u) u.ReadyForDownload))
    End Sub
    Private Sub BTT_DOWN_SITE_Click(sender As Object, e As EventArgs) Handles BTT_DOWN_SITE.Click
        DownloadSiteFull(True)
    End Sub
    Private Sub BTT_DOWN_ALL_FULL_Click(sender As Object, e As EventArgs) Handles BTT_DOWN_ALL_FULL.Click
        Downloader.AddRange(Settings.Users)
    End Sub
    Private Sub BTT_DOWN_SITE_FULL_Click(sender As Object, e As EventArgs) Handles BTT_DOWN_SITE_FULL.Click
        DownloadSiteFull(False)
    End Sub
    Private Sub DownloadSiteFull(ByVal ReadyForDownloadOnly As Boolean)
        Using f As New SiteSelectionForm(Settings.LatestDownloadedSites.ValuesList)
            f.ShowDialog()
            If f.DialogResult = DialogResult.OK Then
                Settings.LatestDownloadedSites.Clear()
                Settings.LatestDownloadedSites.AddRange(f.SelectedSites)
                Settings.LatestDownloadedSites.Update()
                If f.SelectedSites.Count > 0 Then
                    Downloader.AddRange(Settings.Users.SelectMany(Function(ByVal u As IUserData) As IEnumerable(Of IUserData)
                                                                      If u.IsCollection Then
                                                                          Return DirectCast(u, UserDataBind).Collections.
                                                                                 Where(Function(uu) f.SelectedSites.Contains(uu.Site) And
                                                                                                    (Not ReadyForDownloadOnly Or uu.ReadyForDownload))
                                                                      ElseIf f.SelectedSites.Contains(u.Site) And
                                                                             (Not ReadyForDownloadOnly Or u.ReadyForDownload) Then
                                                                          Return {u}
                                                                      Else
                                                                          Return New IUserData() {}
                                                                      End If
                                                                  End Function))
                End If
            End If
        End Using
    End Sub
#End Region
    Private Sub BTT_DOWN_VIDEO_Click(sender As Object, e As EventArgs) Handles BTT_DOWN_VIDEO.Click
        DownloadVideoByURL()
    End Sub
    Private Sub BTT_DOWN_STOP_Click(sender As Object, e As EventArgs) Handles BTT_DOWN_STOP.Click
        Downloader.Stop()
    End Sub
#End Region
#Region "View"
    Private Sub BTT_VIEW_LARGE_Click(sender As Object, e As EventArgs) Handles BTT_VIEW_LARGE.Click
        ApplyViewPattern(ViewModes.IconLarge)
    End Sub
    Private Sub BTT_VIEW_SMALL_Click(sender As Object, e As EventArgs) Handles BTT_VIEW_SMALL.Click
        ApplyViewPattern(ViewModes.IconSmall)
    End Sub
    Private Sub BTT_VIEW_LIST_Click(sender As Object, e As EventArgs) Handles BTT_VIEW_LIST.Click
        ApplyViewPattern(ViewModes.List)
    End Sub
    Private Sub BTT_VIEW_DETAILS_Click(sender As Object, e As EventArgs) Handles BTT_VIEW_DETAILS.Click
        ApplyViewPattern(ViewModes.Details)
    End Sub
    Private Sub ApplyViewPattern(ByVal v As ViewModes)
        LIST_PROFILES.View = v
        Dim b As Boolean = Not (Settings.ViewMode.Value = v)
        Settings.ViewMode.Value = v

        BTT_VIEW_LARGE.Checked = v = ViewModes.IconLarge
        BTT_VIEW_SMALL.Checked = v = ViewModes.IconSmall
        BTT_VIEW_LIST.Checked = v = ViewModes.List
        BTT_VIEW_DETAILS.Checked = v = ViewModes.Details

        If v = View.Details Then
            LIST_PROFILES.Columns(0).Width = -2
            LIST_PROFILES.FullRowSelect = True
            LIST_PROFILES.GridLines = True
        End If

        If b Then
            If Settings.ViewModeIsPicture Then
                With LIST_PROFILES : .LargeImageList.Images.Clear() : .SmallImageList.Images.Clear() : End With
            End If
            RefillList()
        End If
    End Sub
#End Region
#Region "View Site"
    Private Sub BTT_SITE_ALL_Click(sender As Object, e As EventArgs) Handles BTT_SITE_ALL.Click
        Settings.SelectedSites.Clear()
        Settings.SelectedSites.Update()
        If Not BTT_SITE_ALL.Checked Then RefillList()
        BTT_SITE_ALL.Checked = True
        BTT_SITE_SPECIFIC.Checked = False
    End Sub
    Private Sub BTT_SITE_SPECIFIC_Click(sender As Object, e As EventArgs) Handles BTT_SITE_SPECIFIC.Click
        Using f As New SiteSelectionForm(Settings.SelectedSites.ValuesList)
            f.ShowDialog()
            If f.DialogResult = DialogResult.OK Then
                Settings.SelectedSites.Clear()
                Settings.SelectedSites.AddRange(f.SelectedSites)
                Settings.SelectedSites.Update()
                BTT_SITE_SPECIFIC.Checked = Settings.SelectedSites.Count > 0
                BTT_SITE_ALL.Checked = Settings.SelectedSites.Count = 0
                RefillList()
            End If
        End Using
    End Sub
#End Region
#Region "View menu"
    Private Sub BTT_SHOW_ALL_Click(sender As Object, e As EventArgs) Handles BTT_SHOW_ALL.Click
        SetShowButtonsCheckers(ShowingModes.All)
    End Sub
    Private Sub BTT_SHOW_REGULAR_Click(sender As Object, e As EventArgs) Handles BTT_SHOW_REGULAR.Click
        SetShowButtonsCheckers(ShowingModes.Regular)
    End Sub
    Private Sub BTT_SHOW_TEMP_Click(sender As Object, e As EventArgs) Handles BTT_SHOW_TEMP.Click
        SetShowButtonsCheckers(ShowingModes.Temporary)
    End Sub
    Private Sub BTT_SHOW_FAV_Click(sender As Object, e As EventArgs) Handles BTT_SHOW_FAV.Click
        SetShowButtonsCheckers(ShowingModes.Favorite)
    End Sub
    Private Sub BTT_SHOW_DELETED_Click(sender As Object, e As EventArgs) Handles BTT_SHOW_DELETED.Click
        SetShowButtonsCheckers(ShowingModes.Deleted)
    End Sub
    Private Sub BTT_SHOW_SUSPENDED_Click(sender As Object, e As EventArgs) Handles BTT_SHOW_SUSPENDED.Click
        SetShowButtonsCheckers(ShowingModes.Suspended)
    End Sub
    Private Sub BTT_SHOW_LABELS_Click(sender As Object, e As EventArgs) Handles BTT_SHOW_LABELS.Click
        Dim b As Boolean = OpenLabelsForm(Settings.Labels.Current)
        Dim m As ShowingModes
        If Settings.Labels.Current.Count = 0 Then
            m = Settings.ShowingMode.Value
            If m = ShowingModes.Labels Then m = ShowingModes.All
        Else
            m = ShowingModes.Labels
        End If
        SetShowButtonsCheckers(m, Settings.ShowingMode.Value = ShowingModes.Labels And m = ShowingModes.Labels And b)
    End Sub
    Private Sub BTT_SHOW_NO_LABELS_Click(sender As Object, e As EventArgs) Handles BTT_SHOW_NO_LABELS.Click
        SetShowButtonsCheckers(ShowingModes.NoLabels)
    End Sub
    Private Sub BTT_SHOW_EXCLUDED_LABELS_Click(sender As Object, e As EventArgs) Handles BTT_SHOW_EXCLUDED_LABELS.Click
        Dim b As Boolean = OpenLabelsForm(Settings.Labels.Excluded)
        SetExcludedButtonChecker()
        If b Then RefillList()
    End Sub
    Private Sub BTT_SHOW_EXCLUDED_LABELS_IGNORE_Click(sender As Object, e As EventArgs) Handles BTT_SHOW_EXCLUDED_LABELS_IGNORE.Click
        Settings.Labels.ExcludedIgnore.Value = Not Settings.Labels.ExcludedIgnore.Value
        If Settings.Labels.Excluded.Count > 0 Then RefillList()
        SetExcludedButtonChecker()
    End Sub
    Private Sub BTT_SHOW_SHOW_GROUPS_Click(sender As Object, e As EventArgs) Handles BTT_SHOW_SHOW_GROUPS.Click
        Settings.ShowGroupsInsteadLabels.Value = Not Settings.ShowGroupsInsteadLabels.Value
        If Settings.ShowingMode.Value = ShowingModes.Labels Then RefillList()
        SetShowButtonsCheckers(Settings.ShowingMode.Value)
    End Sub
    Private Sub SetShowButtonsCheckers(ByVal m As ShowingModes, Optional ByVal ForceRefill As Boolean = False)
        BTT_SHOW_ALL.Checked = m = ShowingModes.All
        BTT_SHOW_REGULAR.Checked = m = ShowingModes.Regular
        BTT_SHOW_TEMP.Checked = m = ShowingModes.Temporary
        BTT_SHOW_FAV.Checked = m = ShowingModes.Favorite
        BTT_SHOW_DELETED.Checked = m = ShowingModes.Deleted
        BTT_SHOW_SUSPENDED.Checked = m = ShowingModes.Suspended
        BTT_SHOW_LABELS.Checked = m = ShowingModes.Labels
        BTT_SHOW_NO_LABELS.Checked = m = ShowingModes.NoLabels
        BTT_SHOW_SHOW_GROUPS.Checked = Settings.ShowGroupsInsteadLabels
        SetExcludedButtonChecker()
        With Settings
            If Not m = ShowingModes.Labels Then .Labels.Current.Clear() : .Labels.Current.Update()
            If Not .ShowingMode.Value = m Or ForceRefill Then
                .ShowingMode.Value = m
                RefillList()
            Else
                .ShowingMode.Value = m
            End If
        End With
        BTT_DOWN_ALL.Enabled = m = ShowingModes.All
    End Sub
    Private Sub SetExcludedButtonChecker()
        BTT_SHOW_EXCLUDED_LABELS.Checked = Settings.Labels.Excluded.Count > 0
        BTT_SHOW_EXCLUDED_LABELS_IGNORE.Checked = Settings.Labels.ExcludedIgnore
    End Sub
    Private Function OpenLabelsForm(ByRef ll As XML.Base.XMLValuesCollection(Of String)) As Boolean
        Using f As New LabelsForm(ll) With {.WithDeleteButton = True}
            f.ShowDialog()
            If f.DialogResult = DialogResult.OK Then
                With ll : .Clear() : .AddRange(f.LabelsList) : .Update() : End With
                Return True
            Else
                Return False
            End If
        End Using
    End Function
    Private Sub BTT_SHOW_LIMIT_DATES_Click(sender As Object, e As EventArgs) Handles BTT_SHOW_LIMIT_DATES.Click
        Dim r As Boolean = False
        Dim snd As Action(Of Date?) = Sub(ByVal d As Date?)
                                          With Settings.LastUpdatedDate
                                              If .HasValue And d.HasValue Then
                                                  r = Not .Value.Date = d.Value.Date
                                              Else
                                                  r = True
                                              End If
                                          End With
                                          Settings.LastUpdatedDate = d
                                      End Sub
        Using f As New FDatePickerForm(Settings.LastUpdatedDate)
            f.ShowDialog()
            Select Case f.DialogResult
                Case DialogResult.Abort : snd(Nothing)
                Case DialogResult.OK : snd(f.SelectedDate)
            End Select
        End Using
        BTT_SHOW_LIMIT_DATES.Checked = Settings.LastUpdatedDate.HasValue
        If r Then RefillList()
    End Sub
#End Region
    Private Sub BTT_LOG_Click(sender As Object, e As EventArgs) Handles BTT_LOG.Click
        MyMainLOG_ShowForm(Settings.Design)
    End Sub
    Private Sub BTT_DONATE_Click(sender As Object, e As EventArgs) Handles BTT_DONATE.Click
        Try : Process.Start("https://github.com/AAndyProgram/SCrawler/blob/main/HowToSupport.md") : Catch : End Try
    End Sub
#Region "List functions"
    Private _LatestSelected As Integer = -1
    Private Sub LIST_PROFILES_SelectedIndexChanged(sender As Object, e As EventArgs) Handles LIST_PROFILES.SelectedIndexChanged
        Dim a As Action = Sub()
                              If LIST_PROFILES.SelectedIndices.Count > 0 Then
                                  _LatestSelected = LIST_PROFILES.SelectedIndices(0)
                              Else
                                  _LatestSelected = -1
                              End If
                          End Sub
        If LIST_PROFILES.InvokeRequired Then LIST_PROFILES.Invoke(a) Else a.Invoke
    End Sub
    Private Sub LIST_PROFILES_MouseDoubleClick(sender As Object, e As MouseEventArgs) Handles LIST_PROFILES.MouseDoubleClick
        OpenFolder()
    End Sub
#Region "Context"
    Private Sub BTT_CONTEXT_DOWN_Click(sender As Object, e As EventArgs) Handles BTT_CONTEXT_DOWN.Click
        DownloadSelectedUser(DownUserLimits.None)
    End Sub
    Private Sub BTT_CONTEXT_DOWN_LIMITED_Click(sender As Object, e As EventArgs) Handles BTT_CONTEXT_DOWN_LIMITED.Click
        DownloadSelectedUser(DownUserLimits.Number)
    End Sub
    Private Sub BTT_CONTEXT_DOWN_DATE_LIMIT_Click(sender As Object, e As EventArgs) Handles BTT_CONTEXT_DOWN_DATE_LIMIT.Click
        DownloadSelectedUser(DownUserLimits.Date)
    End Sub
    Private Sub BTT_CONTEXT_EDIT_Click(sender As Object, e As EventArgs) Handles BTT_CONTEXT_EDIT.Click
        EditSelectedUser()
    End Sub
    Private Sub BTT_CONTEXT_DELETE_Click(sender As Object, e As EventArgs) Handles BTT_CONTEXT_DELETE.Click
        DeleteSelectedUser()
    End Sub
    Private Sub BTT_CONTEXT_FAV_Click(sender As Object, e As EventArgs) Handles BTT_CONTEXT_FAV.Click
        Dim users As List(Of IUserData) = GetSelectedUserArray()
        If AskForMassReplace(users, "Favorite") Then
            users.ForEach(Sub(u)
                              u.Favorite = Not u.Favorite
                              u.UpdateUserInformation()
                              UserListUpdate(u, False)
                          End Sub)
        End If
    End Sub
    Private Sub BTT_CONTEXT_TEMP_Click(sender As Object, e As EventArgs) Handles BTT_CONTEXT_TEMP.Click
        Dim users As List(Of IUserData) = GetSelectedUserArray()
        If AskForMassReplace(users, "Temporary") Then
            users.ForEach(Sub(u)
                              u.Temporary = Not u.Temporary
                              u.UpdateUserInformation()
                              UserListUpdate(u, False)
                          End Sub)
        End If
    End Sub
    Private Sub BTT_CONTEXT_READY_Click(sender As Object, e As EventArgs) Handles BTT_CONTEXT_READY.Click
        Dim users As List(Of IUserData) = GetSelectedUserArray()
        If AskForMassReplace(users, "Ready for download") Then
            Dim r As Boolean = MsgBoxE({"What state do you want to set for selected users", "Select ready state"}, vbQuestion,,, {"Not Ready", "Ready"}).Index
            users.ForEach(Sub(u)
                              u.ReadyForDownload = r
                              u.UpdateUserInformation()
                          End Sub)
        End If
    End Sub
    Private Sub BTT_CONTEXT_GROUPS_Click(sender As Object, e As EventArgs) Handles BTT_CONTEXT_GROUPS.Click
        Try
            Dim users As List(Of IUserData) = GetSelectedUserArray()
            If users.ListExists Then
                Dim l As List(Of String) = ListAddList(Nothing, users.SelectMany(Function(u) u.Labels), LAP.NotContainsOnly)
                Using f As New LabelsForm(l) With {.MultiUser = True}
                    f.ShowDialog()
                    If f.DialogResult = DialogResult.OK Then
                        Dim _lp As LAP = LAP.NotContainsOnly
                        If f.MultiUserClearExists Then _lp += LAP.ClearBeforeAdd
                        Dim lp As New ListAddParams(_lp)
                        users.ForEach(Sub(ByVal u As IUserData)
                                          If u.IsCollection Then
                                              With DirectCast(u, UserDataBind)
                                                  If .Count > 0 Then .Collections.ForEach(Sub(uu) uu.Labels.ListAddList(f.LabelsList, lp))
                                              End With
                                          Else
                                              u.Labels.ListAddList(f.LabelsList, lp)
                                          End If
                                          u.UpdateUserInformation()
                                      End Sub)
                    End If
                End Using
            Else
                MsgBoxE("No one user does not detected", vbExclamation)
            End If
        Catch ex As Exception
            ErrorsDescriber.Execute(EDP.ShowAllMsg, ex, "[ChangeUserGroups]")
        End Try
    End Sub
    Private Sub BTT_CONTEXT_SCRIPT_Click(sender As Object, e As EventArgs) Handles BTT_CONTEXT_SCRIPT.Click
        Try
            Dim users As List(Of IUserData) = GetSelectedUserArray()
            If users.ListExists Then
                Dim ans% = MsgBoxE({"You want to change the script usage for selected users." & vbCr &
                                    "Which script usage mode do you want to set?",
                                    "Change script usage"}, vbExclamation,,, {"Use", "Do not use", "Cancel"})
                If ans < 2 Then
                    Dim s As Boolean = IIf(ans = 0, True, False)
                    users.ForEach(Sub(ByVal u As IUserData)
                                      Dim b As Boolean = u.ScriptUse = s
                                      u.ScriptUse = s
                                      If Not b Then u.UpdateUserInformation()
                                  End Sub)
                    MsgBoxE($"Script mode was set to [{IIf(s, "Use", "Do not use")}] for all selected users")
                Else
                    MsgBoxE("Operation canceled")
                End If
            Else
                MsgBoxE("Users not selected", vbExclamation)
            End If
        Catch ex As Exception
            ErrorsDescriber.Execute(EDP.LogMessageValue, ex, "Change script usage")
        End Try
    End Sub
    Private Function AskForMassReplace(ByVal users As List(Of IUserData), ByVal param As String) As Boolean
        Dim u$ = users.ListIfNothing.Take(20).Select(Function(uu) uu.Name).ListToString(, vbCr)
        If Not u.IsEmptyString And users.ListExists(21) Then u &= vbCr & "..."
        Return users.ListExists AndAlso (users.Count = 1 OrElse MsgBoxE({$"Do you really want to change [{param}] for {users.Count} users?{vbCr}{vbCr}{u}",
                                                                         "Users' parameters change"},
                                                                        MsgBoxStyle.Exclamation + MsgBoxStyle.YesNo) = MsgBoxResult.Yes)
    End Function
    Private Sub BTT_CHANGE_IMAGE_Click(sender As Object, e As EventArgs) Handles BTT_CHANGE_IMAGE.Click
        Dim user As IUserData = GetSelectedUser()
        If Not user Is Nothing Then
            Dim f As SFile = SFile.SelectFiles(user.File.CutPath(IIf(user.IsCollection, 2, 1)), False, "Select new user picture",
                                               "Pictures|*.jpeg;*.jpg;*.png;*.webp|GIF|*.gif|All Files|*.*").FirstOrDefault
            If Not f.IsEmptyString Then
                user.SetPicture(f)
                UserListUpdate(user, False)
            End If
        End If
    End Sub
    Private Sub BTT_CONTEXT_ADD_TO_COL_Click(sender As Object, e As EventArgs) Handles BTT_CONTEXT_ADD_TO_COL.Click
        If Settings.CollectionsPath.Value.IsEmptyString Then
            MsgBoxE("Collection path does not set", MsgBoxStyle.Exclamation)
        Else
            Dim user As IUserData = GetSelectedUser()
            If Not user Is Nothing Then
                If user.IsCollection Then
                    MsgBoxE("Collection can not be added to collection!", MsgBoxStyle.Critical)
                Else
                    Using f As New CollectionEditorForm(user.CollectionName)
                        f.ShowDialog()
                        If f.DialogResult = DialogResult.OK Then
                            With Settings
                                Dim fCol As Predicate(Of IUserData) = Function(u) u.IsCollection And u.CollectionName = f.Collection
                                Dim i% = .Users.FindIndex(fCol)
                                Dim Added As Boolean = i < 0
                                If i < 0 Then
                                    .Users.Add(New UserDataBind(f.Collection))
                                    CollectionHandler(DirectCast(.Users.Last, UserDataBind))
                                    i = .Users.Count - 1
                                End If
                                Try
                                    DirectCast(.Users(i), UserDataBind).Add(user)
                                    RemoveUserFromList(user)
                                    i = .Users.FindIndex(fCol)
                                    If i >= 0 Then UserListUpdate(.Users(i), Added)
                                    MsgBoxE($"[{user.Name}] was added to collection [{f.Collection}]")
                                Catch ex As InvalidOperationException
                                    i = .Users.FindIndex(fCol)
                                    If i >= 0 AndAlso DirectCast(.Users(i), UserDataBind).Count = 0 Then .Users(i).Dispose() : .Users.RemoveAt(i)
                                End Try
                            End With
                        End If
                    End Using
                End If
            End If
        End If
    End Sub
    Private Sub BTT_CONTEXT_COL_MERGE_Click(sender As Object, e As EventArgs) Handles BTT_CONTEXT_COL_MERGE.Click
        Dim user As IUserData = GetSelectedUser()
        If Not user Is Nothing Then
            If user.IsCollection Then
                If DirectCast(user, UserDataBind).DataMerging Then
                    MsgBoxE("Collection files are already merged")
                Else
                    If MsgBoxE({"Do you really want to merge collection files into one folder?" & vbNewLine &
                                "This action is not turnable!", "Merging files"},
                               MsgBoxStyle.Exclamation + MsgBoxStyle.YesNo) = MsgBoxResult.Yes Then
                        DirectCast(user, UserDataBind).DataMerging = True
                    End If
                End If
            Else
                MsgBoxE("This is not collection!", MsgBoxStyle.Exclamation)
            End If
        End If
    End Sub
    Private Sub BTT_CONTEXT_CHANGE_FOLDER_Click(sender As Object, e As EventArgs) Handles BTT_CONTEXT_CHANGE_FOLDER.Click
        Try
            Dim users As List(Of IUserData) = GetSelectedUserArray()
            If users.ListExists Then
                If users.Count = 1 Then
                    Dim CutOption% = 1
                    Dim _IsCollection As Boolean = False
                    With users(0)
                        If .IsCollection Then
                            _IsCollection = True
                            With DirectCast(.Self, UserDataBind)
                                If .Count = 0 Then
                                    Throw New ArgumentOutOfRangeException("Collection", "Collection is empty")
                                Else
                                    With DirectCast(.Collections(0), UserDataBase)
                                        If Not .User.Merged Then CutOption = 2
                                    End With
                                End If
                            End With
                        End If
                    End With

                    Dim CurrDir As SFile = users(0).File.CutPath(CutOption)
                    Dim NewDest As SFile = SFile.GetPath(InputBoxE($"Enter a new destination for user [{users(0)}]", "Change user folder", CurrDir.Path))
                    If Not NewDest.IsEmptyString Then
                        If MsgBoxE({$"You are changing the user's [{users(0)}] destination" & vbCr &
                                    $"Current destination: {CurrDir.PathNoSeparator}" & vbCr &
                                    $"New destination: {NewDest.Path}",
                                    "Changing user destination"}, MsgBoxStyle.Exclamation,,, {"Confirm", "Cancel"}) = 0 Then
                            If Not NewDest.IsEmptyString AndAlso
                               (Not NewDest.Exists(SFO.Path, False) OrElse
                                    (
                                        SFile.GetFiles(NewDest,, IO.SearchOption.AllDirectories, EDP.ThrowException).ListIfNothing.Count = 0 AndAlso
                                        NewDest.Delete(SFO.Path, Settings.DeleteMode, EDP.ThrowException) AndAlso
                                        Not NewDest.Exists(SFO.Path, False)
                                    )
                               ) Then
                                NewDest.CutPath.Exists(SFO.Path)
                                IO.Directory.Move(CurrDir.Path, NewDest.Path)
                                Dim ApplyChanges As Action(Of IUserData) = Sub(ByVal __user As IUserData)
                                                                               With DirectCast(__user, UserDataBase)
                                                                                   Dim u As UserInfo = .User.Clone
                                                                                   Settings.UsersList.Remove(u)
                                                                                   Dim d As SFile = Nothing
                                                                                   If _IsCollection Then d = SFile.GetPath($"{NewDest.PathWithSeparator}{u.File.PathFolders(1).LastOrDefault}")
                                                                                   If d.IsEmptyString Then d = NewDest
                                                                                   u.SpecialPath = d.PathWithSeparator
                                                                                   u.UpdateUserFile()
                                                                                   Settings.UpdateUsersList(u)
                                                                                   .User = u.Clone
                                                                                   .UpdateUserInformation()
                                                                               End With
                                                                           End Sub
                                If users(0).IsCollection Then
                                    With DirectCast(users(0), UserDataBind)
                                        For Each user In .Collections : ApplyChanges(user) : Next
                                    End With
                                Else
                                    ApplyChanges(users(0))
                                End If
                                MsgBoxE($"User data has been moved")
                            Else
                                MsgBoxE($"Unable to move user data to new destination [{NewDest}]{vbCr}Operation canceled", MsgBoxStyle.Critical)
                            End If
                        Else
                            MsgBoxE("Operation canceled")
                        End If
                    Else
                        MsgBoxE("You have not entered a new destination" & vbCr & "Operation canceled", MsgBoxStyle.Exclamation)
                    End If
                Else
                    MsgBoxE("You have selected multiple users. You can change the folder only for one user!", MsgBoxStyle.Critical)
                End If
            Else
                MsgBoxE("No one user selected", MsgBoxStyle.Exclamation)
            End If
        Catch ex As Exception
            ErrorsDescriber.Execute(EDP.ShowAllMsg, ex, "Error while moving user")
        End Try
    End Sub
    Private Sub BTT_CONTEXT_OPEN_PATH_Click(sender As Object, e As EventArgs) Handles BTT_CONTEXT_OPEN_PATH.Click
        OpenFolder()
    End Sub
    Private Sub BTT_CONTEXT_OPEN_SITE_Click(sender As Object, e As EventArgs) Handles BTT_CONTEXT_OPEN_SITE.Click
        Dim user As IUserData = GetSelectedUser()
        If Not user Is Nothing Then user.OpenSite()
    End Sub
    Private Sub BTT_CONTEXT_INFO_Click(sender As Object, e As EventArgs) Handles BTT_CONTEXT_INFO.Click
        Dim user As IUserData = GetSelectedUser()
        If Not user Is Nothing Then MsgBoxE(DirectCast(user, UserDataBase).GetUserInformation())
    End Sub
    Private Sub USER_CONTEXT_VisibleChanged(sender As Object, e As EventArgs) Handles USER_CONTEXT.VisibleChanged
        Try
            If USER_CONTEXT.Visible Then
                Dim user As IUserData = GetSelectedUser()
                If Not user Is Nothing AndAlso user.IsCollection Then
                    With DirectCast(user, UserDataBind)
                        BTT_CONTEXT_DOWN.DropDownItems.AddRange(.ContextDown)
                        BTT_CONTEXT_EDIT.DropDownItems.AddRange(.ContextEdit)
                        BTT_CONTEXT_DELETE.DropDownItems.AddRange(.ContextDelete)
                        BTT_CONTEXT_OPEN_PATH.DropDownItems.AddRange(.ContextPath)
                        BTT_CONTEXT_OPEN_SITE.DropDownItems.AddRange(.ContextSite)
                    End With
                End If
            Else
                BTT_CONTEXT_DOWN.DropDownItems.Clear()
                BTT_CONTEXT_EDIT.DropDownItems.Clear()
                BTT_CONTEXT_DELETE.DropDownItems.Clear()
                BTT_CONTEXT_OPEN_PATH.DropDownItems.Clear()
                BTT_CONTEXT_OPEN_SITE.DropDownItems.Clear()
            End If
        Catch ex As Exception
        End Try
    End Sub
#End Region
#End Region
    Private Function GetSelectedUser() As IUserData
        If _LatestSelected >= 0 And _LatestSelected <= LIST_PROFILES.Items.Count - 1 Then
            Dim k$ = LIST_PROFILES.Items(_LatestSelected).Name
            Dim i% = Settings.Users.FindIndex(Function(u) u.Key = k)
            If i >= 0 Then
                Return Settings.Users(i)
            Else
                MsgBoxE("User not found", MsgBoxStyle.Critical)
            End If
        End If
        Return Nothing
    End Function
    Private Function GetSelectedUserArray() As List(Of IUserData)
        Try
            With LIST_PROFILES
                If .SelectedIndices.Count > 0 Then
                    Dim l As New List(Of IUserData)
                    Dim k$
                    Dim indx%
                    For i% = 0 To .SelectedIndices.Count - 1
                        k = .Items(.SelectedIndices(i)).Name
                        indx = Settings.Users.FindIndex(Function(u) u.Key = k)
                        If i >= 0 Then l.Add(Settings.Users(indx))
                    Next
                    Return l
                End If
            End With
            Return New List(Of IUserData)
        Catch ex As Exception
            Return ErrorsDescriber.Execute(EDP.SendInLog + EDP.ReturnValue, ex, "[MainFrame.GetSelectedUserArray]", New List(Of IUserData))
        End Try
    End Function
    Private Overloads Sub RemoveUserFromList(ByVal _User As IUserData)
        RemoveUserFromList(LIST_PROFILES.Items.IndexOfKey(_User.Key), _User.Key)
    End Sub
    Private Overloads Sub RemoveUserFromList(ByVal _Index As Integer, ByVal Key As String)
        Dim a As Action = Sub()
                              With LIST_PROFILES
                                  If _Index >= 0 Then
                                      .Items.RemoveAt(_Index)
                                      If Settings.ViewModeIsPicture Then
                                          Dim ImgIndx%
                                          Select Case Settings.ViewMode.Value
                                              Case View.LargeIcon
                                                  ImgIndx = .LargeImageList.Images.IndexOfKey(Key)
                                                  If ImgIndx >= 0 Then .LargeImageList.Images.RemoveAt(_Index)
                                              Case View.SmallIcon
                                                  ImgIndx = .SmallImageList.Images.IndexOfKey(Key)
                                                  If ImgIndx >= 0 Then .SmallImageList.Images.RemoveAt(_Index)
                                          End Select
                                      End If
                                  End If
                              End With
                          End Sub
        If LIST_PROFILES.InvokeRequired Then LIST_PROFILES.Invoke(a) Else a.Invoke
    End Sub
    Private Sub EditSelectedUser()
        Dim user As IUserData = GetSelectedUser()
        If Not user Is Nothing Then
            On Error Resume Next
            If user.IsCollection Then
                If USER_CONTEXT.Visible Then USER_CONTEXT.Hide()
                MsgBoxE($"This is collection!{vbNewLine}Edit collections does not allowed!", vbExclamation)
            Else
                Using f As New UserCreatorForm(user)
                    f.ShowDialog()
                    If f.DialogResult = DialogResult.OK Then UserListUpdate(user, False)
                End Using
            End If
        End If
    End Sub
    Private Sub DeleteSelectedUser()
        Try
            Dim users As List(Of IUserData) = GetSelectedUserArray()
            If users.ListExists Then
                If USER_CONTEXT.Visible Then USER_CONTEXT.Hide()
                Dim ugn As Func(Of IUserData, String) = Function(u) $"{IIf(u.IsCollection, "Collection", "User")}: {u.Name}"
                Dim m As New MMessage(users.Select(ugn).ListToString(, vbNewLine), "Users deleting",
                                      {New Messaging.MsgBoxButton("Delete and ban") With {.ToolTip = "Users and their data will be deleted and added to the blacklist"},
                                       New Messaging.MsgBoxButton("Delete user only and ban") With {
                                            .ToolTip = "Users will be deleted and added to the blacklist (user data will not be deleted)"},
                                       New Messaging.MsgBoxButton("Delete and ban with reason") With {
                                            .ToolTip = "Users and their data will be deleted and added to the blacklist with set a reason to delete"},
                                       New Messaging.MsgBoxButton("Delete user only and ban with reason") With {
                                            .ToolTip = "Users will be deleted and added to the blacklist with set a reason to delete (user data will not be deleted)"},
                                       New Messaging.MsgBoxButton("Delete") With {.ToolTip = "Delete users and their data"},
                                       New Messaging.MsgBoxButton("Delete user only") With {.ToolTip = "Delete users but keep data"}, "Cancel"},
                                      MsgBoxStyle.Exclamation) With {.ButtonsPerRow = 2, .ButtonsPlacing = MMessage.ButtonsPlacings.StartToEnd}
                m.Text = $"The following users ({users.Count}) will be deleted:{vbNewLine}{m.Text}"
                Dim result% = MsgBoxE(m)
                If result < 6 Then
                    Dim removedUsers As New List(Of String)
                    Dim keepData As Boolean = Not (result Mod 2) = 0
                    Dim banUser As Boolean = result < 4
                    Dim setReason As Boolean = banUser And result > 1
                    Dim leftUsers As New List(Of String)
                    Dim l As New ListAddParams(LAP.NotContainsOnly)
                    Dim b As Boolean = False
                    Dim reason$ = String.Empty
                    If setReason Then reason = InputBoxE("Enter a deletion reason:", "Deletion reason")
                    For Each user In users
                        If keepData Then
                            If banUser Then Settings.BlackList.ListAddValue(New UserBan(user.Name, reason), l) : b = True
                            If user.IsCollection Then
                                With DirectCast(user, UserDataBind)
                                    If .Count > 0 Then .Collections.ForEach(Sub(c) Settings.UsersList.Remove(DirectCast(c, UserDataBase).User))
                                End With
                            Else
                                Settings.UsersList.Remove(DirectCast(user, UserDataBase).User)
                            End If
                            Settings.Users.Remove(user)
                            Settings.UpdateUsersList()
                            RemoveUserFromList(user)
                            removedUsers.Add(ugn(user))
                            user.Dispose()
                        Else
                            If user.Delete > 0 Then
                                If banUser Then Settings.BlackList.ListAddValue(New UserBan(user.Name, reason), l) : b = True
                                RemoveUserFromList(user)
                                removedUsers.Add(ugn(user))
                            Else
                                leftUsers.Add(ugn(user))
                            End If
                        End If
                    Next
                    m = New MMessage(String.Empty, "Users deleting")
                    If removedUsers.Count = users.Count Then
                        If removedUsers.Count = 1 Then
                            m.Text = "User deleted"
                        Else
                            m.Text = "All users were deleted"
                        End If
                    ElseIf removedUsers.Count = 0 Then
                        m.Text = "No one user deleted!"
                        m.Style = MsgBoxStyle.Critical
                    Else
                        m.Text = $"The following users were deleted:{vbNewLine}{removedUsers.ListToString(, vbNewLine)}{vbNewLine.StringDup(2)}"
                        m.Text &= $"The following users were NOT deleted:{vbNewLine}{leftUsers.ListToString(, vbNewLine)}"
                        m.Style = MsgBoxStyle.Exclamation
                    End If
                    If b Then Settings.UpdateBlackList()
                    MsgBoxE(m)
                Else
                    MsgBoxE("Operation canceled")
                End If
            End If
        Catch ex As Exception
            ErrorsDescriber.Execute(EDP.LogMessageValue, ex, "Error on trying to delete user / collection")
        End Try
    End Sub
    Friend Sub UserRemovedFromCollection(ByVal User As IUserData)
        If LIST_PROFILES.Items.Count = 0 OrElse Not LIST_PROFILES.Items.ContainsKey(User.Key) Then UserListUpdate(User, True)
    End Sub
    Friend Sub CollectionRemoved(ByVal User As IUserData)
        With LIST_PROFILES.Items
            If .Count > 0 AndAlso .ContainsKey(User.Key) Then .RemoveByKey(User.Key)
        End With
    End Sub
    Private Enum DownUserLimits : None : Number : [Date] : End Enum
    Private Sub DownloadSelectedUser(ByVal UseLimits As DownUserLimits)
        Dim users As List(Of IUserData) = GetSelectedUserArray()
        If users.ListExists Then
            Dim l%? = Nothing
            Dim d As Date? = Nothing
            If UseLimits = DownUserLimits.Number Then
                Do
                    l = AConvert(Of Integer)(InputBoxE("Enter top posts limit for downloading:", "Download limit", 10), AModes.Var, Nothing)
                    If l.HasValue Then
                        Select Case MsgBoxE(New MMessage($"You are set up downloading top [{l.Value}] posts", "Download limit",
                                            {"Confirm", "Try again", "Disable limit", "Cancel"}) With {.ButtonsPerRow = 2}).Index
                            Case 0 : Exit Do
                            Case 2 : l = Nothing : Exit Do
                            Case 3 : GoTo CancelDownloadingOperation
                        End Select
                    Else
                        Select Case MsgBoxE({"You are not set up downloading limit", "Download limit"},,,, {"Confirm", "Try again", "Cancel"}).Index
                            Case 0 : Exit Do
                            Case 2 : GoTo CancelDownloadingOperation
                        End Select
                    End If
                Loop
            ElseIf UseLimits = DownUserLimits.Date Then
                Do
                    Using fd As New FDatePickerForm(Nothing)
                        fd.ShowDialog()
                        If fd.DialogResult = DialogResult.OK Then
                            d = fd.SelectedDate
                        ElseIf fd.DialogResult = DialogResult.Abort Then
                            d = Nothing
                        End If
                    End Using
                    If d.HasValue Then
                        Select Case MsgBoxE(New MMessage($"You are set up downloading posts until [{d.Value.Date.ToStringDate(ADateTime.Formats.BaseDate)}]",
                                                         "Download limit",
                                            {"Confirm", "Try again", "Disable limit", "Cancel"}) With {.ButtonsPerRow = 2}).Index
                            Case 0 : Exit Do
                            Case 2 : d = Nothing : Exit Do
                            Case 3 : GoTo CancelDownloadingOperation
                        End Select
                    Else
                        Select Case MsgBoxE({"You are not set up a date limit", "Download limit"},,,, {"Confirm", "Try again", "Cancel"}).Index
                            Case 0 : Exit Do
                            Case 2 : GoTo CancelDownloadingOperation
                        End Select
                    End If

                Loop
            End If
            If USER_CONTEXT.Visible Then USER_CONTEXT.Hide()
            GoTo ResumeDownloadingOperation
CancelDownloadingOperation:
            MsgBoxE("Operation canceled")
            Exit Sub
ResumeDownloadingOperation:
            If users.Count = 1 Then
                users(0).DownloadTopCount = l
                users(0).DownloadToDate = d
                Downloader.Add(users(0))
            Else
                Dim uStr$ = users.Select(Function(u) u.ToString()).ListToString(, vbNewLine)
                If MsgBoxE({$"You are select {users.Count} users' profiles{vbNewLine}Do you want to download all of them?{vbNewLine.StringDup(2)}" &
                            $"Selected users:{vbNewLine}{uStr}", "A few users selected"},
                           MsgBoxStyle.Question + MsgBoxStyle.YesNo) = MsgBoxResult.Yes Then
                    users.ForEach(Sub(u) u.DownloadTopCount = l)
                    Downloader.AddRange(users)
                End If
            End If
        End If
    End Sub
    Private Sub OpenFolder()
        Dim user As IUserData = GetSelectedUser()
        If Not user Is Nothing Then user.OpenFolder()
    End Sub
#End Region
    Private Sub Info_OnUserLooking(ByVal Key As String)
        FocusUser(Key, True)
    End Sub
    Private Sub FocusUser(ByVal Key As String, Optional ByVal ActivateMe As Boolean = False)
        Dim i% = LIST_PROFILES.Items.IndexOfKey(Key)
        If i < 0 Then
            i = Settings.Users.FindIndex(Function(u) u.Key = Key)
            If i >= 0 Then
                UserListUpdate(Settings.Users(i), True)
                i = LIST_PROFILES.Items.IndexOfKey(Key)
            End If
        End If
        If i >= 0 Then
            LIST_PROFILES.Select()
            LIST_PROFILES.SelectedIndices.Clear()
            With LIST_PROFILES.Items(i) : .Selected = True : .Focused = True : End With
            LIST_PROFILES.EnsureVisible(i)
            If ActivateMe Then
                If Visible Then BringToFront() Else Visible = True
            End If
        End If
    End Sub
    Friend Sub User_OnUserUpdated(ByVal User As IUserData)
        UserListUpdate(User, False)
    End Sub
    Private _LogColorChanged As Boolean = False
    Private Sub Downloader_UpdateJobsCount(ByVal TotalCount As Integer)
        Dim a As Action = Sub() LBL_JOBS_COUNT.Text = IIf(TotalCount = 0, String.Empty, $"[Jobs {TotalCount}]")
        If Toolbar_BOTTOM.InvokeRequired Then Toolbar_BOTTOM.Invoke(a) Else a.Invoke
        If Not _LogColorChanged AndAlso Not MyMainLOG.IsEmptyString Then
            a = Sub() BTT_LOG.ControlChangeColor(False)
            If Toolbar_TOP.InvokeRequired Then Toolbar_TOP.Invoke(a) Else a.Invoke
            _LogColorChanged = True
        ElseIf _LogColorChanged And MyMainLOG.IsEmptyString Then
            a = Sub() BTT_LOG.ControlChangeColor(SystemColors.Control, SystemColors.ControlText)
            If Toolbar_TOP.InvokeRequired Then Toolbar_TOP.Invoke(a) Else a.Invoke
            _LogColorChanged = False
        End If
    End Sub
    Private Sub Downloader_OnDownloading(ByVal Value As Boolean)
        Dim a As Action = Sub() BTT_DOWN_STOP.Enabled = Value
        If Toolbar_TOP.InvokeRequired Then Toolbar_TOP.Invoke(a) Else a.Invoke
    End Sub
    Private Sub NotificationMessage(ByVal Message As String)
        If Settings.ShowNotifications Then TrayIcon.ShowBalloonTip(2000, TrayIcon.BalloonTipTitle, Message, ToolTipIcon.Info)
    End Sub
    Private Sub BTT_PR_INFO_Click(sender As Object, e As EventArgs) Handles BTT_PR_INFO.Click
        If MyProgressForm.Visible Then MyProgressForm.BringToFront() Else MyProgressForm.Show()
    End Sub
End Class
