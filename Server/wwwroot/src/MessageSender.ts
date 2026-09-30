import { ViewerApp } from "./App.js";
import {
    CtrlAltDelDto,
    KeyDownDto,
    KeyPressDto,
    KeyUpDto,
    MouseDownDto,
    MouseMoveDto,
    MouseUpDto,
    MouseWheelDto,
    SelectScreenDto,
    TapDto,
    ToggleAudioDto,
    ToggleBlockInputDto,
    TextTransferDto,
    FileDto,
    WindowsSessionsDto,
    EmptyDto,
    FrameReceivedDto
} from "./Interfaces/Dtos.js";
import { CreateGUID } from "./Utilities.js";
import { FileTransferProgress } from "./UI.js";
import { DtoType } from "./Enums/DtoType.js";
import { RemoteControlMode } from "./Enums/RemoteControlMode.js";

interface PendingMouseMove {
    percentX: number;
    percentY: number;
}

export class MessageSender {
    private PendingMouseMove: PendingMouseMove = null;
    private MouseMoveFlushInFlight = false;
    private PendingFrameReceivedTimestamp: number = null;
    private FrameAckFlushInFlight = false;

    async GetWindowsSessions() {
        if (ViewerApp.Mode == RemoteControlMode.Unattended) {
            var dto = new WindowsSessionsDto();
            await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.WindowsSessions);
        }
    }
    async ChangeWindowsSession(sessionId: number) {
        await ViewerApp.ViewerHubConnection.ChangeWindowsSession(sessionId);
    }
    async SendFrameReceived(timestamp: number) {
        this.PendingFrameReceivedTimestamp = timestamp;
        if (!this.FrameAckFlushInFlight) {
            this.FrameAckFlushInFlight = true;
            void this.FlushFrameReceived();
        }
    }
    async SendSelectScreen(displayName: string) {
        var dto = new SelectScreenDto(displayName);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.SelectScreen);
    }
    async SendMouseMove(percentX: number, percentY: number) {
        this.PendingMouseMove = { percentX, percentY };
        if (!this.MouseMoveFlushInFlight) {
            this.MouseMoveFlushInFlight = true;
            void this.FlushMouseMoves();
        }
    }
    async SendMouseDown(button: number, percentX: number, percentY: number) {
        var dto = new MouseDownDto(button, percentX, percentY);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.MouseDown);
    }
    async SendMouseUp(button: number, percentX: number, percentY: number) {
        var dto = new MouseUpDto(button, percentX, percentY);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.MouseUp);
    }
    async SendTap(percentX: number, percentY: number) {
        var dto = new TapDto(percentX, percentY);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.Tap);
    }
    async SendMouseWheel(deltaX: number, deltaY: number) {
        var dto = new MouseWheelDto(deltaX, deltaY);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.MouseWheel);
    }
    async SendKeyDown(key: string) {
        var dto = new KeyDownDto(key);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.KeyDown);
    }
    async SendKeyUp(key: string) {
        var dto = new KeyUpDto(key);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.KeyUp);
    }
    async SendKeyPress(key: string) {
        var dto = new KeyPressDto(key);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.KeyPress);
    }
    async SendSetKeyStatesUp() {
        var dto = new EmptyDto();
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.SetKeyStatesUp);
    }
    async SendCtrlAltDel() {
        var dto = new CtrlAltDelDto();
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.CtrlAltDel);
        await ViewerApp.ViewerHubConnection.InvokeCtrlAltDel();
    }

    async SendOpenFileTransferWindow() {
        var dto = new EmptyDto();
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.OpenFileTransferWindow);
    }

    async SendFile(buffer: Uint8Array, fileName: string) {
        var messageId = CreateGUID();
        let dto = new FileDto(null, fileName, messageId, false, true);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.File);

        for (var i = 0; i < buffer.byteLength; i += 50_000) {
            let dto = new FileDto(buffer.slice(i, i + 50_000), fileName, messageId, false, false);
            await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.File);

            if (i > 0) {
                FileTransferProgress.value = i / buffer.byteLength;
            }
        }

        dto = new FileDto(null, fileName, messageId, true, false);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.File);
    }

    async SendToggleAudio(toggleOn: boolean) {
        var dto = new ToggleAudioDto(toggleOn);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.ToggleAudio);
    };
    async SendToggleBlockInput(toggleOn: boolean) {
        var dto = new ToggleBlockInputDto(toggleOn);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.ToggleBlockInput);
    }

    async SendTextTransfer(text: string, typeText: boolean) {
        var dto = new TextTransferDto(text, typeText);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.TextTransfer);
    }

    private async FlushMouseMoves(): Promise<void> {
        try {
            while (this.PendingMouseMove !== null) {
                const next = this.PendingMouseMove;
                this.PendingMouseMove = null;
                const dto = new MouseMoveDto(next.percentX, next.percentY);
                await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.MouseMove);
            }
        }
        catch (ex) {
            console.warn("Unable to flush latest mouse move.", ex);
        }
        finally {
            this.MouseMoveFlushInFlight = false;
            if (this.PendingMouseMove !== null) {
                this.MouseMoveFlushInFlight = true;
                void this.FlushMouseMoves();
            }
        }
    }

    private async FlushFrameReceived(): Promise<void> {
        try {
            while (this.PendingFrameReceivedTimestamp !== null) {
                const timestamp = this.PendingFrameReceivedTimestamp;
                this.PendingFrameReceivedTimestamp = null;
                const dto = new FrameReceivedDto(timestamp);
                await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.FrameReceived);
            }
        }
        catch (ex) {
            console.warn("Unable to flush latest frame acknowledgement.", ex);
        }
        finally {
            this.FrameAckFlushInFlight = false;
            if (this.PendingFrameReceivedTimestamp !== null) {
                this.FrameAckFlushInFlight = true;
                void this.FlushFrameReceived();
            }
        }
    }
}
